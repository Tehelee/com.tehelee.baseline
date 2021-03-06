using System.Collections;
using System.Collections.Generic;
using System.Net;

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;

namespace Tehelee.Baseline.Networking
{
	public enum ReadResult : byte
	{
		Skipped,    // Not utilized by this listener
		Processed,  // Used by this listener, but non exclusively.
		Consumed,   // Used by this listener, and stops all further listener checks.
		Error       // A problem was encountered, store information into readHandlerErrorMessage
	}

	public class Shared : MonoBehaviour
	{
		////////////////////////////////
		//	Attributes

		#region Attributes

		public const string LoopbackAddress = "127.0.0.1";

		public string address = LoopbackAddress;
		public ushort port = 16448;

		public bool openOnEnable = false;
		public bool debug = false;
		public bool registerSingleton = true;

		public NetworkParamters networkParameters = new NetworkParamters();

		#endregion

		////////////////////////////////
		//	Properties

		#region Properties

		public virtual string networkScopeLabel { get { return "Shared"; } }

		public float connectTimeout => ( networkParameters.connectTimeoutMS * networkParameters.maxConnectAttempts ) * 0.001f;

		public bool open { get; private set; }

		public ushort networkId { get; protected set; }

		#endregion

		////////////////////////////////
		//	Members

		#region Members

		public Pipeline pipeline;

		public NetworkDriver driver;

		#endregion

		////////////////////////////////
		//	Mono Methods

		#region Mono Methods

		protected virtual void Awake() { }

		protected virtual void OnEnable() { }

		protected virtual void OnDisable() { }

		protected virtual void OnDestroy() { }

		protected virtual void Update()
		{
			if( open )
			{
				NetworkUpdate();
			}
		}

		#endregion

		////////////////////////////////
		//	Open & Close

		#region Open & Close

		public virtual void Open()
		{
			if( open )
				return;

			pingTimingsByNetworkId.Clear();
			usernamesByNetworkId.Clear();
			adminIds.Clear();

			foreach( System.Type type in builtInPacketTypes )
				if( !object.Equals( null, type ) )
					Packet.Register( this, type );

			RegisterPacketDatas();

			Dictionary<string, string> args = new Dictionary<string, string>();
			args.Add( "networkAddress", null );
			args.Add( "networkPort", null );

			Utils.GetArgsFromDictionary( ref args );

			if( !string.IsNullOrEmpty( args[ "networkAddress" ] ) )
				this.address = args[ "networkAddress" ];

			this.address = this.address ?? LoopbackAddress;

			if( !string.IsNullOrEmpty( args[ "networkPort" ] ) )
			{
				ushort port;
				if( ushort.TryParse( args[ "networkPort" ], out port ) )
					this.port = port;
			}

			if( networkParameters.useSimulator )
			{
				driver = NetworkDriver.Create
				(
					new NetworkDataStreamParameter
					{
						size = 0
					},
					new ReliableUtility.Parameters
					{
						WindowSize = networkParameters.maxPacketCount
					},
					new NetworkConfigParameter
					{
						connectTimeoutMS = networkParameters.connectTimeoutMS,
						disconnectTimeoutMS = networkParameters.disconnectTimeoutMS,
						maxConnectAttempts = networkParameters.maxConnectAttempts
					},
					( SimulatorUtility.Parameters ) networkParameters
				);
				
				pipeline = new Pipeline( driver, true );
			}
			else
			{
				driver = NetworkDriver.Create
				(
					new NetworkDataStreamParameter
					{
						size = 0
					},
					new ReliableUtility.Parameters
					{
						WindowSize = networkParameters.maxPacketCount
					},
					new NetworkConfigParameter
					{
						connectTimeoutMS = networkParameters.connectTimeoutMS,
						disconnectTimeoutMS = networkParameters.disconnectTimeoutMS,
						maxConnectAttempts = networkParameters.maxConnectAttempts
					}
				);

				pipeline = new Pipeline( driver );
			}

			open = true;

			if( debug )
				Debug.LogFormat( "{0}.Open() {1} on {2} with {3}", networkScopeLabel, this.address, this.port, networkParameters.ToString() );
		}

		public virtual void Close()
		{
			if( !open )
				return;

			foreach( System.Type type in builtInPacketTypes )
				if( !object.Equals( null, type ) )
					Packet.Unregister( this, type );

			open = false;

			pipeline = default;

			driver.Dispose();

			UnregisterPacketDatas();

			if( debug )
				Debug.LogFormat( "{0}.Close()", networkScopeLabel );
		}

		#endregion

		////////////////////////////////
		//	Packet Datas

		#region Packet Datas

		private List<System.Type> builtInPacketTypes = new List<System.Type>()
		{
			typeof( Packets.Administration ),
			typeof( Packets.Bundle ),
			typeof( Packets.Handshake ),
			typeof( Packets.Loopback ),
			typeof( Packets.Password ),
			typeof( Packets.Ping ),
			typeof( Packets.ServerInfo ),
			typeof( Packets.Username )
		};
		
		public List<DesignData.PacketData> packetDatas = new List<DesignData.PacketData>();

		protected virtual void RegisterPacketDatas()
		{
			foreach( DesignData.PacketData packetData in packetDatas )
			{
				LoadPacketData( packetData, true );
			}
		}

		protected virtual void UnregisterPacketDatas()
		{
			foreach( DesignData.PacketData packetData in packetDatas )
			{
				UnloadPacketData( packetData, true );
			}
		}

		public void LoadPacketData( DesignData.PacketData packetData, bool skipAdd = false )
		{
			string[] packetTypeNames = packetData.packetTypeNames;
			foreach( string packetTypeName in packetTypeNames )
			{
				System.Type packetType = Utils.FindType( packetTypeName );

				if( !object.Equals( null, packetType ) )
					Packet.Register( this, packetType );
			}

			if( !skipAdd && !packetDatas.Contains( packetData ) )
				packetDatas.Add( packetData );
		}

		public void UnloadPacketData( DesignData.PacketData packetData, bool skipRemove = false )
		{
			if( packetDatas.Contains( packetData ) )
			{
				string[] packetTypeNames = packetData.packetTypeNames;
				foreach( string packetTypeName in packetTypeNames )
				{
					System.Type packetType = Utils.FindType( packetTypeName );

					if( !object.Equals( null, packetType ) )
						Packet.Unregister( this, packetType );
				}

				if( !skipRemove )
					packetDatas.Remove( packetData );
			}
		}

		#endregion

		////////////////////////////////
		//	Pipeline

		#region Pipeline

		[System.Serializable]
		public struct Pipeline
		{
			public NetworkPipeline reliable;
			public NetworkPipeline unreliable;

			public Pipeline( NetworkDriver driver, bool useSimulator = false )
			{
				if( useSimulator )
				{
					reliable = driver.CreatePipeline( typeof( ReliableSequencedPipelineStage ), typeof( SimulatorPipelineStage ) );
					unreliable = driver.CreatePipeline( typeof( UnreliableSequencedPipelineStage ), typeof( SimulatorPipelineStage ) );
				}
				else
				{
					reliable = driver.CreatePipeline( typeof( ReliableSequencedPipelineStage ) );
					unreliable = driver.CreatePipeline( typeof( UnreliableSequencedPipelineStage ) );
				}
			}

			public NetworkPipeline this[ bool reliable ] { get { return reliable ? this.reliable : this.unreliable; } }
		}

		#endregion

		////////////////////////////////
		//	Reliability Error

		#region ReliabilityError

		protected unsafe int GetReliabilityError( NetworkConnection connection )
		{
			NativeArray<byte> readProcessingBuffer = default;
			NativeArray<byte> writeProcessingBuffer = default;
			NativeArray<byte> sharedBuffer = default;

			driver.GetPipelineBuffers( pipeline.reliable, NetworkPipelineStageCollection.GetStageId( typeof( ReliableSequencedPipelineStage ) ), connection, out readProcessingBuffer, out writeProcessingBuffer, out sharedBuffer );

			ReliableUtility.SharedContext* unsafePointer = ( ReliableUtility.SharedContext* ) sharedBuffer.GetUnsafePtr();

			if( unsafePointer->errorCode != 0 )
			{
				int errorId = ( int ) unsafePointer->errorCode;
				return errorId;
			}
			else
			{
				return 0;
			}
		}
		
		#endregion

		////////////////////////////////
		//	Send & Recieve

		#region SendRecieve

		[System.Serializable]
		protected struct PacketQueue
		{
			public Queue<Packet> reliable;
			public Queue<Packet> unreliable;
		}

		protected PacketQueue packetQueue = new PacketQueue() { reliable = new Queue<Packet>(), unreliable = new Queue<Packet>() };

		protected virtual ReadResult InternalRead( NetworkConnection connection, ref PacketReader reader ) => ReadResult.Skipped;

		protected static ushort packetBundleHash { get; private set; } = Packet.Hash( typeof( Packets.Bundle ) );

		public string readHandlerErrorMessage = null;

		public virtual void Send( Packet packet, bool reliable = false )
		{
			if( !open )
				return;

			if( null == packet )
				return;

			if( object.Equals( null, Packet.LookupType( packet.id ) ) )
			{
				Debug.LogErrorFormat( "Packet type ( {0} ) not registered!", packet.GetType().FullName );
				return;
			}

			if( packet.id == packetBundleHash )
			{
				Packets.Bundle packetBundle = ( Packets.Bundle ) packet;
				foreach( Packet bundledPacket in packetBundle.packets )
				{
					UpdateSendMonitors( bundledPacket, reliable );
				}
			}
			else
			{
				UpdateSendMonitors( packet, reliable );
			}
			

			if( reliable )
				packetQueue.reliable.Enqueue( packet );
			else
				packetQueue.unreliable.Enqueue( packet );

			if( debug )
				Debug.LogWarningFormat( "{0}.Write( {1} ) using {2} channel.{3}", networkScopeLabel, packet.GetType().FullName, reliable ? "verified" : "fast", !object.Equals( null, packet.targets ) && packet.targets.Count > 0 ? string.Format( " Sent only to {0} connections.", packet.targets.Count ) : string.Empty );
		}

		private void UpdateSendMonitors( Packet packet, bool reliable )
		{
			if( packetMonitors.ContainsKey( packet.id ) && packetMonitors[ packet.id ] != null )
			{
				Dictionary<int, SendMonitor> listeners = packetMonitors[ packet.id ];

				List<int> keys = new List<int>( listeners.Keys );
				keys.Sort();

				for( int i = 0; i < keys.Count; i++ )
				{
					int key = keys[ i ];
					SendMonitor sendMonitor = listeners[ key ];

					if( sendMonitor != null )
					{
						sendMonitor.Invoke( packet, reliable );
					}
				}
			}
		}

		protected ReadResult Read( NetworkConnection connection, ref DataStreamReader reader )
		{
			NativeArray<byte> readerBytes = new NativeArray<byte>( reader.Length, Allocator.Temp );
			reader.ReadBytes( readerBytes );
			PacketReader packetReader = new PacketReader( readerBytes );
			return Read( connection, ref packetReader );
		}

		protected ReadResult Read( NetworkConnection connection, ref PacketReader reader )
		{
			int beginReadIndex = reader.readIndex;

			ushort packetId = reader.ReadUShort();

			if( !Packet.IsValid( packetId ) )
				return ReadResult.Error;

			if( packetId == packetBundleHash )
			{
				int count = reader.ReadUShort();

				if( debug )
					Debug.LogFormat( "{0}.ReadPacketBundle() with {1} packets.", networkScopeLabel, count );

				for( int i = 0; i < count; i++ )
					if( Read( connection, ref reader ) == ReadResult.Error )
						return ReadResult.Error;

				return ReadResult.Consumed;
			}

			int preInternalIndex = reader.readIndex;
			reader.readIndex = beginReadIndex;

			ReadResult internalReadResult = InternalRead( connection, ref reader );
			switch( internalReadResult )
			{
				default:
					reader.readIndex = preInternalIndex;
					break;

				case ReadResult.Error:
					return ReadResult.Skipped;

				case ReadResult.Consumed:

					if( debug )
						Debug.LogFormat( "{0}.InternalRead( {1} ).", networkScopeLabel, Packet.LookupType( packetId ).FullName );

					return ReadResult.Consumed;
			}
			
			if( packetRoutings.ContainsKey( packetId ) )
			{
				PacketRouting packetRouting = packetRoutings[ packetId ];
				if( object.Equals( null, packetRouting ) )
				{
					packetRoutings.Remove( packetId );
				}
				else
				{
					int preRoutingIndex = reader.readIndex;

					if( packetRouting.Process( this, connection, ref reader ) == ReadResult.Consumed )
						return ReadResult.Consumed;
					else
						reader.readIndex = preRoutingIndex;
				}
			}

			int listenerCount = 0;

			if( packetListeners.ContainsKey( packetId ) )
			{
				if( object.Equals( null, packetListeners[ packetId ] ) )
					return ReadResult.Error;

				listenerCount = packetListeners[ packetId ].Count;
			}
			
			if( listenerCount == 0 )
			{
				if( debug )
					Debug.LogWarningFormat( "{0}.Read( {1} ) [ {2} ] skipped; no associated listeners.", networkScopeLabel, Packet.LookupType( packetId )?.FullName, packetId );
				
				return ReadResult.Skipped;
			}

			if( debug )
				Debug.LogFormat( "{0}.Read( {1} ) with {2} listeners.", networkScopeLabel, Packet.LookupType( packetId ).FullName, listenerCount );

			bool processed = false;

			Dictionary<int, ReadHandler> listeners = packetListeners[ packetId ];
			List<int> keys = new List<int>( listeners.Keys );
			keys.Sort();

			int iterationContext, processedContext = reader.readIndex;
			for( int i = 0; i < keys.Count; i++ )
			{
				int key = keys[ i ];
				ReadHandler readHandler = listeners[ key ];

				iterationContext = reader.readIndex;

				if( readHandler != null )
				{
					ReadResult result = readHandler( connection, ref reader );
					if( result == ReadResult.Skipped )
					{
						reader.readIndex = iterationContext;
					}
					else
					{
						switch( result )
						{
							case ReadResult.Processed:
								if( debug )
									Debug.LogFormat( "{0}.Read( {1} ) processed by listener {2}.", networkScopeLabel, Packet.LookupType( packetId ).FullName, i );
								processedContext = reader.readIndex;
								reader.readIndex = iterationContext;
								processed = true;
								break;
							case ReadResult.Consumed:
								if( debug )
									Debug.LogFormat( "{0}.Read( {1} ) consumed by listener {2}.", networkScopeLabel, Packet.LookupType( packetId ).FullName, i );
								return ReadResult.Consumed;
							case ReadResult.Error:
								Debug.LogErrorFormat( "{0}.Read( {1} ) encountered an error on listener {2}.{3}", networkScopeLabel, Packet.LookupType( packetId ).FullName, i, readHandlerErrorMessage != null ? string.Format( "\nError Message: {0}", readHandlerErrorMessage ) : string.Empty );
								reader.readIndex = iterationContext;
								return ReadResult.Error;
						}
					}
				}
			}

			if( processed )
			{
				reader.readIndex = processedContext;

				return ReadResult.Processed;
			}
			
			if( debug )
				Debug.LogWarningFormat( "{0}.Read( {1} ) failed to be consumed by one of the {2} listeners.", networkScopeLabel, Packet.LookupType( packetId ).FullName, listenerCount );
			
			return ReadResult.Error;
		}

		#endregion

		////////////////////////////////
		//	Processing

		#region Processing

		protected virtual void NetworkUpdate() { }

		protected virtual void QueryForEvents() { }
		protected virtual void SendQueue() { }

		#endregion

		////////////////////////////////
		//	Listeners
		//		Callback On Recieve

		#region Listeners

		public delegate ReadResult ReadHandler( NetworkConnection connection, ref PacketReader reader );

		Dictionary<ushort, Dictionary<int,ReadHandler>> packetListeners = new Dictionary<ushort, Dictionary<int, ReadHandler>>();
		
		public void RegisterListener( System.Type packetType, ReadHandler handler, int priority = 0 )
		{
			RegisterListener( Packet.Hash( packetType ), handler, priority );
		}

		public void RegisterListener( ushort packetId, ReadHandler handler, int priority = 0 )
		{
			if( packetId == 0 )
				return;

			if( !packetListeners.ContainsKey( packetId ) )
			{
				packetListeners.Add( packetId, new Dictionary<int, ReadHandler>() );
			}

			Dictionary<int, ReadHandler> listeners = packetListeners[ packetId ];

			while( listeners.ContainsKey( priority ) )
				priority++;

			packetListeners[ packetId ].Add( priority, handler );
		}

		public void DropListener( System.Type packetType, ReadHandler handler )
		{
			DropListener( Packet.Hash( packetType ), handler );
		}

		public void DropListener( ushort packetId, ReadHandler handler )
		{
			if( packetListeners.ContainsKey( packetId ) && packetListeners[ packetId ].ContainsValue( handler ) )
			{
				Dictionary<int, ReadHandler> listeners = packetListeners[ packetId ];
				List<int> keys = new List<int>( listeners.Keys );
				for( int i = 0; i < keys.Count; i++ )
				{
					int k = keys[ i ];
					if( listeners[ k ] == handler )
					{
						packetListeners[ packetId ].Remove( k );
						break;
					}
				}

				if( packetListeners[ packetId ].Count == 0 )
					packetListeners.Remove( packetId );
			}
		}

		#endregion

		////////////////////////////////
		//	Monitors
		//		Callback On Send

		#region Monitors

		public delegate void SendMonitor( Packet packet, bool reliable );

		Dictionary<ushort, Dictionary<int, SendMonitor>> packetMonitors = new Dictionary<ushort, Dictionary<int, SendMonitor>>();
		
		public void RegisterMonitor( System.Type packetType, SendMonitor handler, int priority = 0 )
		{
			RegisterMonitor( Packet.Hash( packetType ), handler, priority );
		}

		public void RegisterMonitor( ushort packetId, SendMonitor handler, int priority = 0 )
		{
			if( !packetMonitors.ContainsKey( packetId ) )
			{
				packetMonitors.Add( packetId, new Dictionary<int, SendMonitor>() );
			}

			Dictionary<int, SendMonitor> listeners = packetMonitors[ packetId ];

			while( listeners.ContainsKey( priority ) )
				priority++;

			packetMonitors[ packetId ].Add( priority, handler );
		}

		public void DropMonitor( System.Type packetType, SendMonitor handler )
		{
			DropMonitor( Packet.Hash( packetType ), handler );
		}

		public void DropMonitor( ushort packetId, SendMonitor handler )
		{
			if( packetMonitors.ContainsKey( packetId ) && packetMonitors[ packetId ].ContainsValue( handler ) )
			{
				Dictionary<int, SendMonitor> listeners = packetMonitors[ packetId ];
				List<int> keys = new List<int>( listeners.Keys );
				for( int i = 0; i < keys.Count; i++ )
				{
					int k = keys[ i ];
					if( listeners[ k ] == handler )
					{
						packetMonitors[ packetId ].Remove( k );
						break;
					}
				}

				if( packetMonitors[ packetId ].Count == 0 )
					packetMonitors.Remove( packetId );
			}
		}

		#endregion

		////////////////////////////////
		//	Packet Routing
		//		Quick redirect instead of callback

		#region Packet Routing

		public class PacketRouting
		{
			public enum RoutingMode : byte
			{
				Loopback,
				Others,
				All
			}

			public RoutingMode routingMode = RoutingMode.Loopback;
			public bool reliable = false;

			public delegate Packet ConstructPacket( ref PacketReader reader );
			public ConstructPacket constructPacket = null;

			public delegate Packet ConstructPacketEx( NetworkConnection networkConnection, ref PacketReader reader );
			public ConstructPacketEx constructPacketEx = null;

			public PacketRouting() { }
			public PacketRouting( RoutingMode routingMode, bool reliable, ConstructPacket constructPacket )
			{
				this.routingMode = routingMode;
				this.reliable = reliable;
				this.constructPacket = constructPacket;
			}

			public PacketRouting( RoutingMode routingMode, bool reliable, ConstructPacketEx constructPacketEx )
			{
				this.routingMode = routingMode;
				this.reliable = reliable;
				this.constructPacketEx = constructPacketEx;
			}

			public ReadResult Process( Shared shared, NetworkConnection networkConnection, ref PacketReader reader )
			{
				Packet packet = null;

				if( !object.Equals( null, constructPacket ) )
					packet = constructPacket( ref reader );
				else if( !object.Equals( null, constructPacketEx ) )
					packet = constructPacketEx( networkConnection, ref reader );
				else
					return ReadResult.Skipped;

				if( object.Equals( null, packet ) )
					return ReadResult.Skipped;

				switch( routingMode )
				{
					case RoutingMode.Loopback:
						packet.targets.Add( networkConnection );
						break;
					case RoutingMode.Others:
					case RoutingMode.All:
						Server server = ( Server ) shared;
						if( object.Equals( null, server ) )
						{
							packet.targets.AddRange( server.GetNetworkConnections() );
							if( routingMode == RoutingMode.Others )
								packet.targets.Remove( networkConnection );
						}
						else
						{
							packet.targets.Add( networkConnection );
						}
						break;
				}

				shared.Send( packet, reliable );

				return ReadResult.Consumed;
			}
		}

		protected Dictionary<ushort, PacketRouting> packetRoutings = new Dictionary<ushort, PacketRouting>();

		public void RegisterRouting( System.Type type, PacketRouting routingMode )
		{
			ushort hash = Packet.Hash( type );

			if( hash == 0 )
				throw new System.ArgumentException( "The type parameter must be a registered Packet type.", "type" );

			if( packetRoutings.ContainsKey( hash ) )
				packetRoutings[ hash ] = routingMode;
			else
				packetRoutings.Add( hash, routingMode );
		}

		public void DropRouting( System.Type type )
		{
			ushort hash = Packet.Hash( type );

			if( hash == 0 )
				throw new System.ArgumentException( "The type parameter must be a registered Packet type.", "type" );

			if( !packetRoutings.ContainsKey( hash ) )
				throw new System.ArgumentOutOfRangeException( "type", "The packet type did not have an existing routing to remove." );

			packetRoutings.Remove( hash );
		}

		#endregion

		////////////////////////////////
		//	Ping

		#region Ping

		protected Dictionary<ushort, ushort> pingTimingsByNetworkId = new Dictionary<ushort, ushort>();

		public virtual ushort GetPing( ushort networkId ) => ( networkId == 0 || !pingTimingsByNetworkId.ContainsKey( networkId ) ) ? ( ushort ) 0 : pingTimingsByNetworkId[ networkId ];

		protected void SetPing( ushort networkId, ushort pingMS )
		{
			if( pingTimingsByNetworkId.ContainsKey( networkId ) )
				pingTimingsByNetworkId[ networkId ] = pingMS;
			else
				pingTimingsByNetworkId.Add( networkId, pingMS );
		}

		#endregion

		////////////////////////////////
		//	Usernames

		#region Usernames

		protected Dictionary<ushort, string> usernamesByNetworkId = new Dictionary<ushort, string>();

		public virtual string GetUsername( ushort networkId ) => ( networkId == 0 || !usernamesByNetworkId.ContainsKey( networkId ) ) ? string.Empty : usernamesByNetworkId[ networkId ];

		protected void SetUsername( ushort networkId, string username )
		{
			if( usernamesByNetworkId.ContainsKey( networkId ) )
				usernamesByNetworkId[ networkId ] = username;
			else
				usernamesByNetworkId.Add( networkId, username );
		}

		#endregion

		////////////////////////////////
		//	Admins

		#region Admins

		protected HashSet<ushort> adminIds = new HashSet<ushort>();
		public List<ushort> GetAdminIds() => new List<ushort>( adminIds );
		public bool IsAdminId( ushort clientId ) => adminIds.Contains( clientId );

		public virtual bool AdminPromote( ushort clientId )
		{
			if( clientId == 0 )
				return false;

			return adminIds.Add( clientId );
		}

		public virtual bool AdminDemote( ushort clientId )
		{
			if( clientId == 0 )
				return false;

			return adminIds.Remove( clientId );
		}

		#endregion
	}

#if UNITY_EDITOR
	[CustomEditor( typeof( Shared ) )]
	public class EditorShared : EditorUtils.InheritedEditor
	{
		Shared shared;

		ReorderableList packetDatas;

		public override void Setup()
		{
			shared = ( Shared ) target;

			base.Setup();

			packetDatas = EditorUtils.CreateReorderableList
			(
				serializedObject.FindProperty( "packetDatas" ),
				( SerializedProperty element ) => lineHeight * 1.5f,
				( Rect rect, SerializedProperty element ) =>
				{
					EditorUtils.BetterObjectField( new Rect( rect.x, rect.y + lineHeight * 0.25f, rect.width, lineHeight ), new GUIContent(), element, typeof( DesignData.PacketData ) );
				}
			);
		}

		public override float GetInspectorHeight()
		{
			float inspectorHeight = base.GetInspectorHeight();

			inspectorHeight += lineHeight * 1.5f;

			inspectorHeight += packetDatas.CalculateCollapsableListHeight();

			inspectorHeight += lineHeight * 3.5f;

			inspectorHeight += EditorGUI.GetPropertyHeight( this[ "networkParameters" ], true ) + lineHeight * 0.5f;

			return inspectorHeight;
		}

		public override void DrawInspector( ref Rect rect )
		{
			base.DrawInspector( ref rect );

			Rect cRect, bRect = new Rect( rect.x, rect.y, rect.width, lineHeight );

			EditorUtils.DrawDivider( bRect, new GUIContent( "Networking - Shared", "Functionality for both the Server and Client environments." ) );
			bRect.y += lineHeight * 1.5f;

			packetDatas.DrawCollapsableList( ref bRect, new GUIContent( "Packet Datas" ) );

			cRect = new Rect( bRect.x, bRect.y, bRect.width - 80f, lineHeight );
			EditorGUI.PropertyField( cRect, this[ "address" ], new GUIContent( "Address & Port" ) );
			cRect = new Rect( bRect.x + bRect.width - 75f, bRect.y, 75f, lineHeight );
			EditorGUI.PropertyField( cRect, this[ "port" ], new GUIContent( string.Empty, "Port" ) );
			bRect.y += lineHeight * 1.5f;

			cRect = new Rect( bRect.x, bRect.y, ( bRect.width - 20f ) / 3f, lineHeight * 1.5f );
			EditorUtils.BetterToggleField( cRect, new GUIContent( "Auto-Open on Enable", string.Format( "The {0} will immediately attempt to connect on component enable.", shared.networkScopeLabel.ToLower() ) ), this[ "openOnEnable" ] );
			cRect.x += cRect.width + 10f;
			EditorUtils.BetterToggleField( cRect, new GUIContent( "Singleton" ), this[ "registerSingleton" ] );
			cRect.x += cRect.width + 10f;
			EditorUtils.BetterToggleField( cRect, new GUIContent( "Debug" ), this[ "debug" ] );
			bRect.y += lineHeight * 2f;

			bRect.height = EditorGUI.GetPropertyHeight( this[ "networkParameters" ], true );
			EditorGUI.PropertyField( bRect, this[ "networkParameters" ], true );
			bRect.y += bRect.height + lineHeight * 0.5f;
			bRect.height = lineHeight;

			rect.y = bRect.y;
		}

		public override float inspectorPostInspectorOffset => 0f;

		public override float GetPostInspectorHeight() => base.GetPostInspectorHeight() + lineHeight * 3.5f;

		public override void DrawPostInspector( ref Rect rect )
		{
			base.DrawPostInspector( ref rect );

			Rect cRect, bRect = new Rect( rect.x, rect.y, rect.width, lineHeight );

			EditorUtils.DrawDivider( bRect, new GUIContent( "Network - State" ) );
			bRect.y += lineHeight * 1.5f;
			
			cRect = new Rect( bRect.x, bRect.y, ( bRect.width - 10f ) * 0.5f, lineHeight * 1.5f );
			EditorGUI.BeginDisabledGroup( true );
			EditorUtils.BetterToggleField( cRect, new GUIContent( "Is Open" ), shared.open );
			EditorGUI.EndDisabledGroup();
			cRect.x += cRect.width + 10f;
			EditorGUI.BeginDisabledGroup( !Application.isPlaying );
			if( EditorUtils.BetterButton( cRect, new GUIContent( !Application.isPlaying ? "Unavailable In Edit Mode" : shared.open ? "Close" : "Open" ) ) )
			{
				if( shared.open )
					shared.Close();
				else
					shared.Open();
			}
			EditorGUI.EndDisabledGroup();
			bRect.y += lineHeight * 2f;
			rect.y = bRect.y;
		}
	}
#endif
}
