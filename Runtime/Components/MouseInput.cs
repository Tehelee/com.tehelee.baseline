using System.Collections;
using System.Collections.Generic;

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace Tehelee.Baseline.Components
{
	public class MouseInput : MonoBehaviour
	{
		////////////////////////////////
		#region Static

		public static Singleton<MouseInput> singleton = new Singleton<MouseInput>();

		public static bool locked { get; private set; }

		public static int releaseRequests { get; private set; } = 0;

		public static void RequestCursor()
		{
			if( Utils.IsObjectAlive( singleton.instance ) )
				singleton.instance.Release();

			releaseRequests++;
		}

		public static void ReturnCursor()
		{
			if( Utils.IsObjectAlive( singleton.instance ) )
				singleton.instance.Lock();

			releaseRequests--;
		}

		#endregion

		////////////////////////////////
		#region Attributes

		public bool lockOnEnable;

		public Texture2D cursorRegular;
		public Texture2D cursorReleased;

		#endregion

		////////////////////////////////
		#region Properties
		
		public Vector2 delta = Vector2.zero;

		#endregion

		////////////////////////////////
		#region Members

		private bool forceReleased = false;

		#endregion

		////////////////////////////////
		#region Events

		public event System.Action onLock;
		public event System.Action onRelease;

		public event System.Action onPrimaryClick;
		public event System.Action onSecondaryClick;
		public event System.Action onMiddleClick;

		public event System.Action onPrimaryRelease;
		public event System.Action onSecondaryRelease;
		public event System.Action onMiddleRelease;

		public event System.Action onScrollUp;
		public event System.Action onScrollDown;

		#endregion

		////////////////////////////////
		#region Mono Methods

		private void OnEnable()
		{
			singleton.instance = this;

			if( lockOnEnable )
				Lock();
			else
				Release();
		}

		private void OnDisable()
		{
			singleton.instance = null;

			Release();

			Cursor.SetCursor( null, Vector2.zero, CursorMode.Auto );
		}

		private void Update()
		{
			bool _locked = Cursor.lockState == CursorLockMode.Locked;

			if( _locked != locked )
			{
				if( _locked )
					Lock();
				else
					Release();
			}
			else if( Input.GetKeyDown( KeyCode.Escape ) )
			{
				if( locked )
					Release();
			}

			if( locked )
			{
				delta = new Vector2( Input.GetAxisRaw( "Mouse X" ), Input.GetAxisRaw( "Mouse Y" ) );

				if( Input.GetMouseButtonDown( 0 ) )
					onPrimaryClick?.Invoke();

				if( Input.GetMouseButtonDown( 1 ) )
					onSecondaryClick?.Invoke();

				if( Input.GetMouseButtonDown( 2 ) )
					onMiddleClick?.Invoke();

				if( Input.GetMouseButtonUp( 0 ) )
					onPrimaryRelease?.Invoke();

				if( Input.GetMouseButtonUp( 1 ) )
					onSecondaryRelease?.Invoke();

				if( Input.GetMouseButtonUp( 2 ) )
					onMiddleRelease?.Invoke();

				int scrollDelta = Mathf.RoundToInt( Input.mouseScrollDelta.y );
				if( scrollDelta != 0 )
				{
					if( scrollDelta > 0 )
					{
						for( int i = 0; i < scrollDelta; i++ )
							onScrollUp?.Invoke();
					}
					else
					{
						scrollDelta = -scrollDelta;
						for( int i = 0; i < scrollDelta; i++ )
							onScrollDown?.Invoke();
					}
				}
			}
		}

		#endregion

		////////////////////////////////
		#region MouseInput
		
		private void Lock()
		{
			if( releaseRequests > 0 || forceReleased )
				return;

			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;

			UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject( null );

			delta = Vector2.zero;

			locked = true;

			onLock?.Invoke();
		}

		private void Release()
		{
			Cursor.SetCursor( forceReleased ? cursorReleased : cursorRegular, Vector2.zero, CursorMode.Auto );

			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;

			locked = false;
			
			delta = Vector2.zero;

			onRelease?.Invoke();
		}

		public void Toggle()
		{
			if( forceReleased )
			{
				forceReleased = false;
				Lock();
			}
			else
			{
				forceReleased = true;
				Release();
			}
		}

		#endregion
	}
	
#if UNITY_EDITOR
	[CustomEditor( typeof( MouseInput ) )]
	public class EditorCursorLock : EditorUtils.InheritedEditor
	{
		MouseInput mouseInput;

		public override void Setup()
		{
			base.Setup();

			mouseInput = ( MouseInput ) target;
		}

		public override float GetInspectorHeight()
		{
			float inspectorHeight = base.GetInspectorHeight();

			inspectorHeight += lineHeight * 5.5f + 4f;

			if( Application.isPlaying )
				inspectorHeight += lineHeight * 1.5f;

			return inspectorHeight;
		}

		public override void DrawInspector( ref Rect rect )
		{
			base.DrawInspector( ref rect );

			Rect bRect = new Rect( rect.x, rect.y, rect.width, lineHeight );

			EditorUtils.DrawDivider( bRect, new GUIContent( "Mouse Input" ) );
			bRect.y += lineHeight * 1.5f;

			Rect cRect = new Rect( bRect.x, bRect.y, ( bRect.width - 10f ) * 0.5f, lineHeight * 1.5f );
			EditorUtils.BetterToggleField( cRect, new GUIContent( "Lock On Enable" ), this[ "lockOnEnable" ] );
			cRect.x += cRect.width + 10f;

			EditorGUI.BeginDisabledGroup( true );
			EditorUtils.BetterToggleField( cRect, new GUIContent( "Locked" ), MouseInput.locked );
			EditorGUI.EndDisabledGroup();
			bRect.y += lineHeight * 2f;

			EditorUtils.BetterObjectField( bRect, new GUIContent( "Cursor Regular" ), this[ "cursorRegular" ], typeof( Texture2D ) );
			bRect.y += lineHeight + 4f;

			EditorUtils.BetterObjectField( bRect, new GUIContent( "Cursor Released" ), this[ "cursorReleased" ], typeof( Texture2D ) );
			bRect.y += lineHeight;

			if( Application.isPlaying )
			{
				bRect.y += lineHeight * 0.5f;

				EditorGUI.BeginDisabledGroup( true );
				EditorGUI.Vector2Field( bRect, new GUIContent( "Mouse Delta" ), mouseInput.delta );
				EditorGUI.EndDisabledGroup();
				bRect.y += lineHeight;
			}

			// Draw Inspector GUI

			rect.y = bRect.y;
		}
	}
#endif
}