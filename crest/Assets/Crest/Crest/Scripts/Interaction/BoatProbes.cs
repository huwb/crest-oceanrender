﻿// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

// Shout out to @holdingjason who posted a first version of this script here: https://github.com/huwb/crest-oceanrender/pull/100

using System;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Crest
{
    /// <summary>
    /// Boat physics by sampling at multiple probe points.
    /// </summary>
    public partial class BoatProbes : FloatingObjectBase
    {
        [Header("Forces")]
        [Tooltip("Override RB center of mass, in local space."), SerializeField]
        Vector3 _centerOfMass = Vector3.zero;
        [SerializeField, FormerlySerializedAs("ForcePoints")]
        FloaterForcePoints[] _forcePoints = new FloaterForcePoints[] { };

        [Tooltip("Vertical offset for where engine force should be applied."), SerializeField]
        float _forceHeightOffset = 0f;
        [SerializeField]
        float _forceMultiplier = 10f;
        [Tooltip("Width dimension of boat. The larger this value, the more filtered/smooth the wave response will be."), SerializeField]
        float _minSpatialLength = 12f;
        [SerializeField, Range(0, 1)]
        float _turningHeel = 0.35f;

        [Header("Drag")]
        [SerializeField]
        float _dragInWaterUp = 3f;
        [SerializeField]
        float _dragInWaterRight = 2f;
        [SerializeField]
        float _dragInWaterForward = 1f;

        [Header("Engine Power")]
        [SerializeField, FormerlySerializedAs("EnginePower")]
        float _enginePower = 7;
        [SerializeField, FormerlySerializedAs("TurnPower")]
        float _turnPower = 0.5f;

        [Header("Controls")]
        [SerializeField]
        BoatControl _boatControl;

        private const float WATER_DENSITY = 1000;

        public override Vector3 Velocity => _rb.velocity;

        Rigidbody _rb;

        public override float ObjectWidth { get { return _minSpatialLength; } }
        public override bool InWater { get { return true; } }

        float _totalWeight;

        Vector3[] _queryPoints;
        Vector3[] _queryResultDisps;
        Vector3[] _queryResultVels;

        SampleFlowHelper _sampleFlowHelper = new SampleFlowHelper();

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.centerOfMass = _centerOfMass;

            if (OceanRenderer.Instance == null)
            {
                enabled = false;
                return;
            }

            CalcTotalWeight();

            _queryPoints = new Vector3[_forcePoints.Length + 1];
            _queryResultDisps = new Vector3[_forcePoints.Length + 1];
            _queryResultVels = new Vector3[_forcePoints.Length + 1];
        }

        void CalcTotalWeight()
        {
            _totalWeight = 0f;
            foreach (var pt in _forcePoints)
            {
                _totalWeight += pt._weight;
            }
        }

        private void FixedUpdate()
        {
#if UNITY_EDITOR
            // Sum weights every frame when running in editor in case weights are edited in the inspector.
            CalcTotalWeight();
#endif

            if (OceanRenderer.Instance == null)
            {
                return;
            }

            var collProvider = OceanRenderer.Instance.CollisionProvider;

            // Do queries
            UpdateWaterQueries(collProvider);

            var undispPos = transform.position - _queryResultDisps[_forcePoints.Length];
            undispPos.y = OceanRenderer.Instance.SeaLevel;

            var waterSurfaceVel = _queryResultVels[_forcePoints.Length];

            {
                _sampleFlowHelper.Init(transform.position, _minSpatialLength);
                _sampleFlowHelper.Sample(out var surfaceFlow);
                waterSurfaceVel += new Vector3(surfaceFlow.x, 0, surfaceFlow.y);
            }

            // Buoyancy
            FixedUpdateBuoyancy();
            FixedUpdateDrag(waterSurfaceVel);
            FixedUpdateEngine();
        }

        void UpdateWaterQueries(ICollProvider collProvider)
        {
            // Update query points
            for (int i = 0; i < _forcePoints.Length; i++)
            {
                _queryPoints[i] = transform.TransformPoint(_forcePoints[i]._offsetPosition + new Vector3(0, _centerOfMass.y, 0));
            }
            _queryPoints[_forcePoints.Length] = transform.position;

            collProvider.Query(GetHashCode(), ObjectWidth, _queryPoints, _queryResultDisps, null, _queryResultVels);
        }

        void FixedUpdateEngine()
        {
            var forcePosition = _rb.position;

            // Get input. X is steer and Z is throttle. Ignore Y.
            var input = _boatControl ? _boatControl.Input : Vector3.zero;

            _rb.AddForceAtPosition(transform.forward * _enginePower * input.z, forcePosition, ForceMode.Acceleration);

            var rotVec = transform.up + _turningHeel * transform.forward;
            _rb.AddTorque(rotVec * _turnPower * input.x, ForceMode.Acceleration);
        }

        void FixedUpdateBuoyancy()
        {
            var archimedesForceMagnitude = WATER_DENSITY * Mathf.Abs(Physics.gravity.y);

            for (int i = 0; i < _forcePoints.Length; i++)
            {
                var waterHeight = OceanRenderer.Instance.SeaLevel + _queryResultDisps[i].y;
                var heightDiff = waterHeight - _queryPoints[i].y;
                if (heightDiff > 0)
                {
                    _rb.AddForceAtPosition(archimedesForceMagnitude * heightDiff * Vector3.up * _forcePoints[i]._weight * _forceMultiplier / _totalWeight, _queryPoints[i]);
                }
            }
        }

        void FixedUpdateDrag(Vector3 waterSurfaceVel)
        {
            // Apply drag relative to water
            var _velocityRelativeToWater = _rb.velocity - waterSurfaceVel;

            var forcePosition = _rb.position + _forceHeightOffset * Vector3.up;
            _rb.AddForceAtPosition(Vector3.up * Vector3.Dot(Vector3.up, -_velocityRelativeToWater) * _dragInWaterUp, forcePosition, ForceMode.Acceleration);
            _rb.AddForceAtPosition(transform.right * Vector3.Dot(transform.right, -_velocityRelativeToWater) * _dragInWaterRight, forcePosition, ForceMode.Acceleration);
            _rb.AddForceAtPosition(transform.forward * Vector3.Dot(transform.forward, -_velocityRelativeToWater) * _dragInWaterForward, forcePosition, ForceMode.Acceleration);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawCube(transform.TransformPoint(_centerOfMass), Vector3.one * 0.25f);

            for (int i = 0; i < _forcePoints.Length; i++)
            {
                var point = _forcePoints[i];

                var transformedPoint = transform.TransformPoint(point._offsetPosition + new Vector3(0, _centerOfMass.y, 0));

                Gizmos.color = Color.red;
                Gizmos.DrawCube(transformedPoint, Vector3.one * 0.5f);
            }
        }
    }

    [Serializable]
    public class FloaterForcePoints
    {
        [FormerlySerializedAs("_factor")]
        public float _weight = 1f;

        public Vector3 _offsetPosition;
    }

#if UNITY_EDITOR
    public partial class BoatProbes : IValidated
    {
        public override bool Validate(OceanRenderer ocean, ValidatedHelper.ShowMessage showMessage)
        {
            var isValid = base.Validate(ocean, showMessage);

            if (!_boatControl)
            {
                showMessage
                (
                    "<i>BoatProbes</i> has no component deriving from <i>BoatControl</i> assigned. The boat will " +
                    "not respond to input. If this is not intentional, then please add one and assign it to this " +
                    "component.",
                    ValidatedHelper.MessageType.Warning, this
                );
            }

            return isValid;
        }
    }

    [CustomEditor(typeof(BoatProbes), true), CanEditMultipleObjects]
    class BoatProbesEditor : ValidatedEditor { }
#endif

}
