using System.Collections;
using System.Collections.Generic;
using System.Net;

using UnityEngine;

using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Tehelee.Baseline.Networking
{
	public class Client : Shared
	{
		////////////////////////////////
		//	Attributes

		#region Attributes

		public Server server = null;

		[Range( 1f, 10f )]
		public float heartbeatInterval = 5f;
		[Range( 1, 10 )]
		public int pingAverageQueueSize = 3;
		[Range( 0, 10 )]
		public int reconnectAttempts = 3;

		#endregion

		////////////////////////////////
		//	Properties

		#region Properties

		public override string networkScopeLabel => "Client";

		public static Singleton<Client> singleton { get; private set; } = new Singleton<Client>();

		public NetworkConnection connection { get; private set; }

		public float openTime { get; private set; }
		public float lastLoopbackTime { get; private set; }

		public bool isConnected { get; private set; }
		public bool hasNetworkId { get; private set; }
		private bool hasConnected;

		public int failedReconnects { get; private set; }

		protected HashSet<ushort> otherClientIds = new HashSet<ushort>();
		public bool IsValidNetworkId( ushort networkId ) => ( networkId != 0 ) && ( networkId == this.networkId || otherClientIds.Contains( networkId ) );
		public List<ushort> GetOtherClientIds() => new List<ushort>( otherClientIds );
		public List<ushort> GetAllClientIds()
		{
			List<ushort> clientIds = new List<ushort>( otherClientIds );
			clientIds.Insert( 0, networkId );

			return clientIds;
		}

		public string username { get; protected set; }
		
		public bool isAdmin => hasNetworkId && adminIds.Contains( networkId );

		public bool isServerApproved { get; private set; }
		public bool recievedHostInfo { get; private set; }
		[SerializeField]
		private HostInfo _hostInfo = new HostInfo();
		public HostInfo hostInfo => new HostInfo( _hostInfo );

		#endregion

		////////////////////////////////
		//	Events

		#region Events

		public event System.Action onOpen;
		public event System.Action onClose;

		public event System.Action onConnected;
		public event System.Action onDisconnected;

		public event System.Action onRecievedHostInfo;
		public event System.Action onServerApproval;

		public event System.Action<string> onPasswordRequested;
		public event System.Action onConnectTimeout;
		public event System.Action<string> onUnexpectedDisconnect;

		public event System.Action<ushort> onHandshake;

		public event System.Action<ushort> onOtherClientConnected;
		public event System.Action<ushort> onOtherClientDisconnected;
		public event System.Action<ushort, string> onOtherClientRenamed;

		public event System.Action onPingTimingsUpdated;

		public event System.Action<string> onUsernameChanged;

		public event System.Action<ushort> onAdminPromote;
		public event System.Action<ushort> onAdminDemote;

		public event System.Action<string> onAdminShutdown;
		public event System.Action<string> onAdminAlert;
		public event System.Action<string> onAdminRenamed;
		public event System.Action<string> onAdminKicked;
		public event System.Action<string> onAdminBanned;

		#endregion

		////////////////////////////////
		//	Mono Methods

		#region Mono Methods

		protected override void Awake()
		{
			base.Awake();
		}

		protected override void OnEnable()
		{
			base.OnEnable();

			RegisterListener( typeof( Packets.Administration ), OnAdministration );
			RegisterListener( typeof( Packets.Handshake ), OnHandshake );
			RegisterListener( typeof( Packets.Loopback ), OnLoopback );
			RegisterListener( typeof( Packets.Password ), OnPassword );
			RegisterListener( typeof( Packets.Ping ), OnPing );
			RegisterListener( typeof( Packets.ServerInfo ), OnServerInfo );
			RegisterListener( typeof( Packets.Username ), OnUsername );

			if( registerSingleton && object.Equals( null, singleton.instance ) )
				singleton.instance = this;

			if( openOnEnable )
				this.Open();
		}

		protected override void OnDisable()
		{
			if( open )
				this.Close();

			if( object.Equals( this, singleton.instance ) )
				singleton.instance = null;

			DropListener( typeof( Packets.Administration ), OnAdministration );
			DropListener( typeof( Packets.Handshake ), OnHandshake );
			DropListener( typeof( Packets.Loopback ), OnLoopback );
			DropListener( typeof( Packets.Password ), OnPassword );
			DropListener( typeof( Packets.Ping ), OnPing );
			DropListener( typeof( Packets.ServerInfo ), OnServerInfo );
			DropListener( typeof( Packets.Username ), OnUsername );

			base.OnDisable();
		}

		protected override void OnDestroy()
		{
			if( driver.IsCreated && hasConnected )
				driver.Dispose();

			if( object.Equals( singleton.instance, this ) )
				singleton.instance = null;

			base.OnDestroy();
		}

		#endregion

		////////////////////////////////
		//	Open & Close

		#region Open & Close

		public override void Open()
		{
			if( open )
				return;

			////////////////
			// Reset

			networkId = 0;
			otherClientIds.Clear();
			
			hasConnected = false;
			failedReconnects = 0;

			loopbackAverage = 0f;
			loopbackAverageMS = 0;
			loopbackTimings.Clear();

			recievedHostInfo = false;
			_hostInfo = new HostInfo();

			isServerApproved = false;

			openTime = Time.time;

			////////////////
			// Validate

			NetworkEndPoint networkEndPoint = NetworkEndPoint.Parse( this.address ?? string.Empty, this.port );
			if( !networkEndPoint.IsValid )
				return;

			////////////////
			// Open

			base.Open();

			connection = default( NetworkConnection );
			
			connection = driver.Connect( networkEndPoint );

			_IHeartbeat = StartCoroutine( IHeartbeat() );

			onOpen?.Invoke();
		}

		public override void Close()
		{
			if( !open )
				return;

			bool wasConnected = isConnected;

			hasNetworkId = false;
			isConnected = false;

			if( connection.IsCreated )
			{
				connection.Disconnect( driver );
				connection = default( NetworkConnection );
			}

			if( !object.Equals( null, _IHeartbeat ) )
			{
				StopCoroutine( _IHeartbeat );
				_IHeartbeat = null;
			}

			base.Close();

			if( wasConnected )
				onDisconnected?.Invoke();

			onClose?.Invoke();
		}

		#endregion

		////////////////////////////////
		//	NetworkUpdate

		#region NetworkUpdate

		protected override void NetworkUpdate()
		{
			if( !driver.IsCreated )
				return;

			driver.ScheduleUpdate().Complete();

			if( !connection.IsCreated )
				return;

			QueryForEvents();

			// Events *could* result in destruction of these, so now we re-check.
			if( !driver.IsCreated || !connection.IsCreated )
				return;

			NetworkConnection.State connectionState = driver.GetConnectionState( connection );

			switch( connectionState )
			{
				default:
					return;

				case NetworkConnection.State.Disconnected:
					return;

				case NetworkConnection.State.Connected:
					break;

				case NetworkConnection.State.AwaitingResponse:
					Debug.Log( "Awaiting Response" );
					break;
				case NetworkConnection.State.Connecting:
					if( Time.time - connectTimeout > openTime )
					{
						onConnectTimeout?.Invoke();

						if( debug )
							Debug.Log( "Client: Connecting timed out." );

						Close();

						return;
					}
					break;
			}

			SendQueue();
		}

		#endregion

		////////////////////////////////
		//	QueryForEvents

		#region QueryForEvents

		protected override void QueryForEvents()
		{
			DataStreamReader stream;
			NetworkEvent.Type netEventType;
			while( driver.IsCreated && connection.IsCreated && ( netEventType = connection.PopEvent( driver, out stream ) ) != NetworkEvent.Type.Empty )
			{
				if( netEventType == NetworkEvent.Type.Connect )
				{
					if( debug )
						Debug.Log( "Client: Connected to the server." );

					failedReconnects = 0;

					isConnected = true;
					hasConnected = true;
					
					onConnected?.Invoke();
				}
				else if( netEventType == NetworkEvent.Type.Data )
				{
					Read( connection, ref stream );
				}
				else if( netEventType == NetworkEvent.Type.Disconnect )
				{
					if( !hasConnected )
					{
						if( debug )
							Debug.Log( "Client: Connecting timed out." );

						onConnectTimeout?.Invoke();
					}
					else
					{
						if( debug )
							Debug.Log( "Client: Reconnecting failed, server timed out." );

						if( Time.time - lastLoopbackTime < connectTimeout )
							onUnexpectedDisconnect?.Invoke( "Server Closed" );
						else
							onUnexpectedDisconnect?.Invoke( "Server Timed Out" );
					}

					Close();
				}
			}
		}

		#endregion

		////////////////////////////////
		//	SendQueue

		#region SendQueue

		public override void Send( Packet packet, bool reliable = false )
		{
			if( !isConnected )
				return;

			base.Send( packet, reliable );
		}

		protected override void SendQueue()
		{
			Packet packet;

			while( packetQueue.reliable.Count > 0 )
			{
				packet = packetQueue.reliable.Peek();

				DataStreamWriter writer = driver.BeginSend( pipeline.reliable, connection, packet.bytes + 2 );
				
				writer.WriteUShort( packet.id );

				packet.Write( ref writer );

				driver.EndSend( writer );

				int errorId = GetReliabilityError( connection );
				if( errorId != 0 )
				{
					ReliableUtility.ErrorCodes error = ( ReliableUtility.ErrorCodes ) errorId;

					if( error != ReliableUtility.ErrorCodes.OutgoingQueueIsFull )
						Debug.LogErrorFormat( "Reliability Error: {0}", error );
					else
						break;
				}

				packetQueue.reliable.Dequeue();
			}

			while( packetQueue.unreliable.Count > 0 )
			{
				packet = packetQueue.unreliable.Dequeue();

				DataStreamWriter writer = driver.BeginSend( pipeline.unreliable, connection, packet.bytes + 2 );

				writer.WriteUShort( packet.id );

				packet.Write( ref writer );

				driver.EndSend( writer );
			}
		}

		#endregion

		////////////////////////////////
		//	Heartbeat & Loopback

		#region Heartbeat & Loopback

		private Queue<float> loopbackTimings = new Queue<float>();

		public float loopbackAverage { get; private set; } = 0f;
		public ushort loopbackAverageMS { get; private set; } = 0;

		private Coroutine _IHeartbeat = null;
		private IEnumerator IHeartbeat()
		{
			loopbackTimings.Clear();
			loopbackAverage = 0f;
			loopbackAverageMS = 0;

			while( true )
			{
				if( isConnected )
				{
					Send( new Packets.Loopback()
					{
						originTime = Time.time,
						averagePingMS = loopbackAverageMS
					} );
				}

				yield return new WaitForSeconds( heartbeatInterval );
			}
		}

		private ReadResult OnLoopback( NetworkConnection connection, ref PacketReader reader )
		{
			Packets.Loopback packetLoopback = new Packets.Loopback( ref reader );

			float time = Time.time;
			
			lastLoopbackTime = time;
			loopbackTimings.Enqueue( time - packetLoopback.originTime );

			if( loopbackTimings.Count > pingAverageQueueSize )
				loopbackTimings.Dequeue();

			float _pingAverage = 0f;
			foreach( float pingTime in loopbackTimings )
				_pingAverage += pingTime;

			if( loopbackTimings.Count > 1 )
				_pingAverage /= loopbackTimings.Count;

			loopbackAverage = _pingAverage;
			loopbackAverageMS = ( ushort ) Mathf.Max( 1, Mathf.RoundToInt( loopbackAverage * 1000f ) );

			return ReadResult.Consumed;
		}

		#endregion

		////////////////////////////////
		//	Ping

		#region Ping

		private ReadResult OnPing( NetworkConnection connection, ref PacketReader reader )
		{
			Packets.Ping packetPing = new Packets.Ping( ref reader );

			pingTimingsByNetworkId = new Dictionary<ushort, ushort>( packetPing.timingsByNetworkId );
			
			onPingTimingsUpdated?.Invoke();

			return ReadResult.Consumed;
		}

		public override ushort GetPing( ushort clientId ) => clientId == networkId ? loopbackAverageMS : base.GetPing( clientId );

		#endregion

		////////////////////////////////
		//	Handshake & Setup

		#region Handshake & Setup

		private ReadResult OnHandshake( NetworkConnection connection, ref PacketReader reader )
		{
			Packets.Handshake packetHandshake = new Packets.Handshake( ref reader );

			switch( packetHandshake.operation )
			{
				case Packets.Handshake.Operation.AssignSelf:

					AssignSelfId( packetHandshake.networkId );

					break;

				case Packets.Handshake.Operation.CreateOther:

					SetupOtherId( packetHandshake.networkId );

					break;

				case Packets.Handshake.Operation.DestroyOther:

					CleanupOtherId( packetHandshake.networkId );

					break;
			}

			return ReadResult.Consumed;
		}

		protected virtual void AssignSelfId( ushort clientId )
		{
			this.networkId = clientId;

			hasNetworkId = true;

			onHandshake?.Invoke( this.networkId );

			if( debug )
				Debug.LogFormat( "Client handshake completed. NetworkId: {0}", this.networkId );
		}

		protected virtual void SetupOtherId( ushort clientId )
		{
			if( !otherClientIds.Contains( clientId ) )
			{
				otherClientIds.Add( clientId );

				onOtherClientConnected?.Invoke( clientId );

				if( debug )
					Debug.LogFormat( "Other client added. NetworkId: {0}", clientId );
			}
			else
			{
				Debug.LogErrorFormat( "Requested to add other client but it's already present in client hashset! NetworkId: {0}", clientId );
			}
		}

		protected virtual void CleanupOtherId( ushort clientId )
		{
			if( otherClientIds.Remove( clientId ) )
			{
				onOtherClientDisconnected?.Invoke( clientId );

				if( debug )
					Debug.LogFormat( "Other client removed. NetworkId: {0}", clientId );
			}
			else
			{
				Debug.LogErrorFormat( "Requested to remove other client not present in client hashset! NetworkId: {0}", clientId );
			}
		}

		#endregion

		////////////////////////////////
		//	Password

		#region Password

		private ReadResult OnPassword( NetworkConnection networkConnection, ref PacketReader reader )
		{
			Packets.Password packetPassword = new Packets.Password( ref reader );

			if( !string.IsNullOrEmpty( packetPassword.password ) )
			{
				onPasswordRequested?.Invoke( packetPassword.password );

				if( debug )
					Debug.LogFormat( "Password not accepted, reason: {0}", packetPassword.password );

				return ReadResult.Consumed;
			}

			isServerApproved = true;

			OnServerApproval();
			
			onServerApproval?.Invoke();

			return ReadResult.Consumed;
		}

		protected virtual void OnServerApproval()
		{
			if( !string.IsNullOrWhiteSpace( username ) )
				Send( new Packets.Username() { name = username }, true );

			Server server = this.server ?? Server.singleton.instance;
			if( Utils.IsObjectAlive( server ) && server.open )
				server.localHostId = networkId;
		}

		#endregion

		////////////////////////////////
		//	Usernames

		#region Usernames

		public void SetUsername( string username )
		{
			if( string.IsNullOrWhiteSpace( username ) )
				return;

			string oldUsername = this.username;
			username = username.Trim();
			
			Send( new Packets.Username() { name = username }, true );

			this.username = username;

			onUsernameChanged?.Invoke( this.username );
		}

		public override string GetUsername( ushort networkId )
		{
			if( networkId == this.networkId )
				return username;
			else if( usernamesByNetworkId.ContainsKey( networkId ) )
				return usernamesByNetworkId[ networkId ];
			else
				return string.Empty;
		}

		private ReadResult OnUsername( NetworkConnection networkConnection, ref PacketReader reader )
		{
			Packets.Username username = new Packets.Username( ref reader );

			if( username.networkId == networkId )
			{
				this.username = username.name;
				onUsernameChanged?.Invoke( this.username );
			}
			else
			{
				if( !usernamesByNetworkId.ContainsKey( username.networkId ) )
				{
					usernamesByNetworkId.Add( username.networkId, username.name );
					onOtherClientRenamed?.Invoke( username.networkId, username.name );
				}
				else if( !string.Equals( usernamesByNetworkId[ username.networkId ], username.name ) )
				{
					usernamesByNetworkId[ username.networkId ] = username.name;
					onOtherClientRenamed?.Invoke( username.networkId, username.name );
				}
			}

			return ReadResult.Consumed;
		}

		#endregion

		////////////////////////////////
		//	Admins

		#region Admins

		public void AdminAuthorize( string password )
		{
			Send( new Packets.Administration() { operation = Packets.Administration.Operation.Authorize, text = password }, true );
		}

		private bool _AdminPromote( ushort networkId )
		{
			if( !IsValidNetworkId( networkId ) )
				return false;

			if( !base.AdminPromote( networkId ) )
				return false;

			onAdminPromote?.Invoke( networkId );

			return true;
		}

		public new bool AdminPromote( ushort networkId )
		{
			if( !isAdmin || adminIds.Contains( networkId ) || !IsValidNetworkId( networkId ) )
				return false;

			Send( new Packets.Administration() { operation = Packets.Administration.Operation.Promote, networkId = networkId }, true );

			return true;
		}

		private bool _AdminDemote( ushort networkId )
		{
			if( !IsValidNetworkId( networkId ) )
				return false;

			if( !base.AdminDemote( networkId ) )
				return false;

			onAdminDemote?.Invoke( networkId );

			return true;
		}

		public new bool AdminDemote( ushort networkId )
		{
			if( !isAdmin || !adminIds.Contains( networkId ) || !IsValidNetworkId( networkId ) )
				return false;

			Send( new Packets.Administration() { operation = Packets.Administration.Operation.Demote, networkId = networkId }, true );

			return true;
		}

		#endregion

		////////////////////////////////
		//	Administration

		#region Administration

		private ReadResult OnAdministration( NetworkConnection networkConnection, ref PacketReader reader )
		{
			Packets.Administration packetAdministration = new Packets.Administration( ref reader );

			switch( packetAdministration.operation )
			{
				case Packets.Administration.Operation.Shutdown:
					_AdminShutdown( packetAdministration.text );
					break;

				case Packets.Administration.Operation.Alert:
					_AdminAlert( packetAdministration.text );
					break;

				case Packets.Administration.Operation.Promote:
					_AdminPromote( packetAdministration.networkId );
					break;

				case Packets.Administration.Operation.Demote:
					_AdminDemote( packetAdministration.networkId );
					break;

				case Packets.Administration.Operation.Rename:
					_AdminRename( packetAdministration.text );
					break;

				case Packets.Administration.Operation.Kick:
					_AdminBoot( packetAdministration.text );
					break;

				case Packets.Administration.Operation.Ban:
					_AdminBoot( packetAdministration.text, true );
					break;
			}
			
			return ReadResult.Consumed;
		}

		#endregion

		////////////////////////////////
		//	Admin Operations

		#region Admin Operations

		private void _AdminShutdown( string reason )
		{
			onAdminShutdown?.Invoke( reason );

			Close();
		}

		public bool AdminShutdown( string reason )
		{
			if( !isAdmin )
				return false;

			Send( new Packets.Administration() { operation = Packets.Administration.Operation.Shutdown, text = reason }, true );

			return true;
		}

		private void _AdminAlert( string message ) =>
			onAdminAlert?.Invoke( message );

		public bool AdminAlert( string message, ushort networkId = 0 )
		{
			if( !isAdmin || networkId != 0 && !IsValidNetworkId( networkId ) )
				return false;

			if( !string.IsNullOrWhiteSpace( message ) )
				Send( new Packets.Administration() { operation = Packets.Administration.Operation.Alert, networkId = networkId, text = message }, true );

			return true;
		}

		private void _AdminRename( string reason )
		{
			onAdminRenamed?.Invoke( reason );
		}

		public bool AdminRename( ushort networkId, string reason )
		{
			if( !isAdmin || networkId == 0 || !IsValidNetworkId( networkId ) )
				return false;

			Send( new Packets.Administration() { operation = Packets.Administration.Operation.Rename, networkId = networkId, text = reason }, true );

			return true;
		}

		private void _AdminBoot( string reason, bool isBan = false )
		{
			if( isBan )
				onAdminBanned?.Invoke( reason );
			else
				onAdminKicked?.Invoke( reason );

			Close();
		}

		public bool AdminBoot( ushort networkId, string reason, bool isBan = false )
		{
			if( !isAdmin || !IsValidNetworkId( networkId ) )
				return false;

			Send( new Packets.Administration()
			{
				operation = isBan ? Packets.Administration.Operation.Ban : Packets.Administration.Operation.Kick,
				networkId = networkId,
				text = reason
			}, true );

			return true;
		}

		#endregion

		////////////////////////////////
		//	Server Info

		#region ServerInfo

		private ReadResult OnServerInfo( NetworkConnection networkConnection, ref PacketReader reader )
		{
			Packets.ServerInfo packetServerInfo = new Packets.ServerInfo( ref reader );

			_hostInfo = packetServerInfo.GetHostInfo();
			recievedHostInfo = true;

			onRecievedHostInfo?.Invoke();
			
			if( _hostInfo.isPrivate )
			{
				Server server = this.server ?? Server.singleton.instance;
				if( Utils.IsObjectAlive( server ) && server.open )
				{
					Send( new Packets.Password() { password = server.hostInfo.password }, true );
				}
				else
				{
					onPasswordRequested?.Invoke( string.Empty );
				}
			}

			return ReadResult.Consumed;
		}

		#endregion
	}
	
#if UNITY_EDITOR
	[CustomEditor( typeof( Client ) )]
	public class EditorClient : EditorShared
	{
		Client client;

		public override void Setup()
		{
			base.Setup();

			client = ( Client ) target;
		}

		public override float GetInspectorHeight()
		{
			float inspectorHeight = base.GetInspectorHeight();
			inspectorHeight += lineHeight * 6.5f + 8f;
			
			if( client.isConnected )
			{
				inspectorHeight += 4f + lineHeight;

				if( client.hasNetworkId )
					inspectorHeight += 4f + lineHeight;

				if( client.recievedHostInfo )
					inspectorHeight += 4f + EditorGUI.GetPropertyHeight( this[ "_hostInfo" ], true );
			}

			return inspectorHeight;
		}

		public override void DrawInspector( ref Rect rect )
		{
			base.DrawInspector( ref rect );

			Rect bRect = new Rect( rect.x, rect.y, rect.width, lineHeight );

			EditorUtils.DrawDivider( bRect, new GUIContent( "Networking - Client" ) );
			bRect.y += lineHeight * 1.5f;

			EditorUtils.BetterObjectField( bRect, new GUIContent( "Server", "Leave this blank to use the singleton instance." ), this[ "server" ], typeof( Server ), true );
			bRect.y += lineHeight * 1.5f;

			EditorGUI.Slider( bRect, this[ "heartbeatInterval" ], 1f, 10f, new GUIContent( "Heartbeart Interval", "The delay between individual loopback packets used for ping updates and keep-alive heartbeats." ) );
			bRect.y += lineHeight + 4f;

			EditorGUI.IntSlider( bRect, this[ "pingAverageQueueSize" ], 1, 10, new GUIContent( "Ping Averages Count", "The amount of ping results that should be tracked and averaged." ) );
			bRect.y += lineHeight + 4f;

			EditorGUI.IntSlider( bRect, this[ "reconnectAttempts" ], 0, 10, new GUIContent( "Reconnect Attempts", "Amount of times to try reconnecting on unsignaled disconnects." ) );
			bRect.y += lineHeight;

			if( client.isConnected )
			{
				EditorGUI.BeginDisabledGroup( true );
				bRect.y += 4f;

				EditorGUI.IntField( bRect, new GUIContent( "Average Ping" ), client.loopbackAverageMS );
				bRect.y += lineHeight;

				if( client.hasNetworkId )
				{
					bRect.y += 4f;

					EditorGUI.IntField( bRect, new GUIContent( "Network Id" ), client.networkId );
					bRect.y += lineHeight;
				}

				if( client.recievedHostInfo )
				{
					bRect.y += 4f;

					bRect.height = EditorGUI.GetPropertyHeight( this[ "_hostInfo" ], true );
					EditorGUI.PropertyField( bRect, this[ "_hostInfo" ], true );
					bRect.y += bRect.height;
					bRect.height = lineHeight;
				}

				EditorGUI.EndDisabledGroup();
			}

			bRect.y += lineHeight * 0.5f;

			rect.y = bRect.y;
		}
	}
#endif
}