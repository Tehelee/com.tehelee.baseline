using System.Collections;
using System.Collections.Generic;

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace Tehelee.Baseline.Components
{
	public class TransformRotor : MonoBehaviour
	{
		////////////////////////////////
		//	Static

		#region Static

		private static GroupUpdates<TransformRotor> groupUpdates = new GroupUpdates<TransformRotor>( PerformRotation );

		#endregion

		////////////////////////////////
		//	Attributes

		#region Attributes

		public Vector3 rotationAxis = Vector3.up;

		public float rotationsPerSecond = 1f;

		public float rotationAngleSnap = 0f;

		public bool resumeRotationOnEnable = true;

		#endregion

		////////////////////////////////
		//	Members

		#region Members

		private float lastUpdate = -1f;

		private float overflowRotation = 0f;

		#endregion

		////////////////////////////////
		//	Mono Methods

		#region Mono Methods

		private void OnEnable()
		{
			if( !resumeRotationOnEnable )
				lastUpdate = -1f;

			PerformRotation();

			groupUpdates.Register( this );
		}

		private void OnDisable()
		{
			groupUpdates.Drop( this );
		}

		#endregion

		////////////////////////////////
		//	TransformRotor

		#region TransformRotor

		private static void PerformRotation( TransformRotor transformRotor ) => transformRotor.PerformRotation();
		public void PerformRotation()
		{
			float time = Time.time;
			float timeDelta = lastUpdate < 0f ? 0f : time - lastUpdate;

			float angleDelta = timeDelta * rotationsPerSecond * 360f;

			if( rotationAngleSnap > 0f )
			{
				float rotation = overflowRotation;
				rotation += angleDelta;
				if( rotation > rotationAngleSnap )
				{
					float rotationDelta = rotation / rotationAngleSnap;
					int stepCount = Mathf.FloorToInt( rotationDelta );
					rotationDelta -= stepCount;
					overflowRotation = rotationDelta * rotationAngleSnap;
					angleDelta = stepCount * rotationAngleSnap;
				}
				else
				{
					overflowRotation = rotation;
					angleDelta = 0f;
				}
			}

			transform.rotation = Quaternion.AngleAxis( angleDelta, rotationAxis ) * transform.rotation;

			lastUpdate = time;
		}

		#endregion
	}

#if UNITY_EDITOR
	[CustomEditor( typeof( TransformRotor ) )]
	public class EditorTransformRotor : EditorUtils.InheritedEditor
	{
		private bool useCustom = false;

		private enum VectorAxis
		{
			Custom	= 0,
			Up		= 1,
			Down	= 2,
			Right	= 3,
			Left	= 4,
			Forward	= 5,
			Back	= 6,
		}

		private static Vector3[] VectorAxisVectors = new Vector3[]
		{
			Vector3.up,
			-Vector3.up,
			Vector3.right,
			-Vector3.right,
			Vector3.forward,
			-Vector3.forward
		};

		public override void Setup()
		{
			base.Setup();

			Vector3 axis = this[ "rotationAxis" ].vector3Value;
			axis = axis.normalized;

			VectorAxis vectorAxis = VectorAxis.Custom;
			for( int i = 0, iC = VectorAxisVectors.Length; i < iC; i++ )
			{
				if( axis == VectorAxisVectors[ i ] )
				{
					vectorAxis = ( VectorAxis ) ( i + 1 );
					break;
				}
			}

			useCustom = ( vectorAxis == VectorAxis.Custom );
		}

		public override float GetInspectorHeight() => base.GetInspectorHeight() + lineHeight * 8f + 12f;

		public override void DrawInspector( ref Rect rect )
		{
			base.DrawInspector( ref rect );

			Rect bRect = new Rect( rect.x, rect.y, rect.width, lineHeight );

			EditorUtils.DrawDivider( bRect, new GUIContent( "TransformRotor" ) );
			bRect.y += lineHeight * 1.5f;

			bRect.height = lineHeight * 1.5f;
			EditorUtils.BetterToggleField( bRect, new GUIContent( "Resume Rotations On Enable" ), this[ "resumeRotationOnEnable" ] );
			bRect.height = lineHeight;
			bRect.y += lineHeight * 2f;

			Vector3 axis = this[ "rotationAxis" ].vector3Value;
			axis = axis.normalized;

			VectorAxis vectorAxis = VectorAxis.Custom;
			if( !useCustom )
			{
				for( int i = 0, iC = VectorAxisVectors.Length; i < iC; i++ )
				{
					if( axis == VectorAxisVectors[ i ] )
					{
						vectorAxis = ( VectorAxis ) ( i + 1 );
						break;
					}
				}
			}

			EditorGUIUtility.labelWidth = 135f;

			VectorAxis _vectorAxis = ( VectorAxis ) EditorGUI.EnumPopup( bRect, new GUIContent( "Global Axis" ), vectorAxis );
			bRect.y += lineHeight + 4f;
			if( _vectorAxis != vectorAxis )
			{
				vectorAxis = _vectorAxis;

				if( vectorAxis != VectorAxis.Custom )
					axis = VectorAxisVectors[ ( ( int ) vectorAxis ) - 1 ];
				
				useCustom = ( vectorAxis == VectorAxis.Custom );
			}

			EditorGUI.BeginDisabledGroup( vectorAxis != VectorAxis.Custom );
			axis = EditorGUI.Vector3Field( bRect, new GUIContent( "Axis Vector" ), axis );
			EditorGUI.EndDisabledGroup();
			bRect.y += lineHeight * 1.5f;

			this[ "rotationAxis" ].vector3Value = axis;

			EditorGUI.PropertyField( bRect, this[ "rotationsPerSecond" ], new GUIContent( "Rotations Per Second" ) );
			bRect.y += lineHeight + 4f;

			EditorUtils.DrawSnapSlider( bRect, this[ "rotationAngleSnap" ], new GUIContent( "Rotation Angle Step" ), 0f, 360f );
			bRect.y += lineHeight;

			EditorGUIUtility.labelWidth = labelWidth;
			
			rect.y = bRect.y;
		}
	}
#endif
}