﻿using System.Collections;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Tehelee.Baseline.Components
{
	[ExecuteAlways]
	public class MimicTransform : MonoBehaviour
	{
		////////////////////////////////
		//	Attributes
		
		#region Attributes

		public Transform followTarget;

		public bool executeInEditMode = false;
		
		public bool mimicPosition = true;
		public bool mimicRotation = true;
		public bool mimicScale = false;

		public Vector3 offsetPosition = Vector3.zero;
		public Vector3 offsetRotation = Vector3.zero;
		public Vector3 multiplyScale = Vector3.one;

		#endregion
		
		////////////////////////////////
		//	Properties
		
		#region Properties

		public bool hasFollowTarget { get; private set; } = false;

		#endregion
		
		////////////////////////////////
		//	Mono Methods
		
		#region Mono Methods

		protected virtual void OnEnable()
		{
			hasFollowTarget = Utils.IsObjectAlive( followTarget );

			if( Application.isPlaying )
			{
				_IFollow = StartCoroutine( IFollow() );
			}
		}

		protected virtual void OnDisable()
		{
			if( !object.Equals( null, _IFollow ) )
			{
				StopCoroutine( _IFollow );
				_IFollow = null;
			}

			hasFollowTarget = false;
		}

		#if UNITY_EDITOR
		private void Update()
		{
			if( !Application.isPlaying && executeInEditMode )
				UpdateFollow();
		}
		#endif
		
		private void UpdateFollow()
		{
			if( hasFollowTarget )
			{
				Transform t = transform;
				
				if( mimicPosition )
					t.position = followTarget.TransformPoint( offsetPosition );
				if( mimicRotation )
					t.rotation = followTarget.rotation * Quaternion.Euler( offsetRotation );
				if( mimicScale )
				{
					Transform parent = t.parent;
					Vector3 lossyScale = followTarget.TransformVector( multiplyScale );
					t.localScale = Utils.IsObjectAlive( parent ) ? parent.InverseTransformVector( lossyScale ) : lossyScale;
				}
			}
		}
		
		private Coroutine _IFollow = null;
		private IEnumerator IFollow()
		{
			while( true )
			{
				yield return null;
				UpdateFollow();
			}
		}

		#endregion
	}
	
	#if UNITY_EDITOR
	[CustomEditor( typeof( MimicTransform ) )]
	public class EditorMimicTransform : EditorUtils.InheritedEditor
	{
		public override float GetInspectorHeight() => base.GetInspectorHeight() + lineHeight * 8.5f + 8f;

		public override void DrawInspector( ref Rect rect )
		{
			base.DrawInspector( ref rect );

			Rect bRect = new Rect( rect.x, rect.y, rect.width, lineHeight );
			
			EditorUtils.DrawDivider( bRect, new GUIContent( "Mimic Transform" ) );
			bRect.y += lineHeight * 1.5f;

			EditorUtils.BetterObjectField( bRect, new GUIContent( "Follow Target" ), this[ "followTarget" ], typeof( Transform ), true );
			bRect.y += lineHeight * 1.5f;

			Rect cRect = new Rect( bRect.x, bRect.y, ( bRect.width - 30f ) / 4f, lineHeight * 1.5f );

			EditorUtils.BetterToggleField( cRect, new GUIContent( "Edit Update" ), this[ "executeInEditMode" ] );
			cRect.x += cRect.width + 10f;
			
			EditorUtils.BetterToggleField( cRect, new GUIContent( "Position" ), this[ "mimicPosition" ] );
			cRect.x += cRect.width + 10f;
			
			EditorUtils.BetterToggleField( cRect, new GUIContent( "Rotation" ), this[ "mimicRotation" ] );
			cRect.x += cRect.width + 10f;
			
			EditorUtils.BetterToggleField( cRect, new GUIContent( "Scale" ), this[ "mimicScale" ] );
			
			bRect.y += lineHeight * 2.0f;

			EditorGUI.PropertyField( bRect, this[ "offsetPosition" ], new GUIContent( "Offset Position" ) );
			bRect.y += lineHeight + 4f;
			EditorGUI.PropertyField( bRect, this[ "offsetRotation" ], new GUIContent( "Offset Rotation" ) );
			bRect.y += lineHeight + 4f;
			EditorGUI.PropertyField( bRect, this[ "multiplyScale" ], new GUIContent( "Offset Scale" ) );
			bRect.y += lineHeight * 1.5f;

			rect.y = bRect.y;
		}
	}
	#endif
}