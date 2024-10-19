﻿using System.Collections.Generic;
using UnityEngine;

public class CurveHandler : MonoBehaviour
{
    public int sortOrder = 1;
    public List<Vector2> splinePoints = new List<Vector2>();

    [Header("The lower the value the more dense the mesh")]
    [SerializeField] [Range(0.01f, 10f)] private float m_detailLevel;

    [Header("The higher the value the more difficult the track")]
    [SerializeField] [Range(0.05f, 50f)] private float m_difficulty;

    [Header("Number of random points to be generated", order = 0)]
    [Space(-10, order = 1)]
    [Header("for the procedural track", order = 2)]
    [SerializeField] [Range(3, 100)] private int m_pointDensity;

    [Header("X = min x local coordinate, Y = min y local coordinate", order = 0)]
    [Space(-10, order = 1)]
    [Header("Width = max x local coordinate, Height = max y local coordinate", order = 2)]
    [SerializeField] private Rect m_roadBounds;

    [SerializeField] private List<Vector2> m_convexhull = new List<Vector2>();
    [SerializeField] private List<Vector2> m_randPoints = new List<Vector2>();
    public class ClockwiseComparer : IComparer<Vector2>
    {
        private Vector2 m_origin;
        private int m_sortOrder;
        public Vector2 Origin { get { return m_origin; } set { m_origin = value; } }

        public ClockwiseComparer(Vector2 _origin, int _sortOrder)
        {
            m_origin = _origin;
            m_sortOrder = _sortOrder;
        }
        public int Compare(Vector2 _first, Vector2 _second)
        {
            return IsClockwise(_first, _second, m_origin);
        }
        public int IsClockwise(Vector2 _first, Vector2 _second, Vector2 _origin)
        {
            if (_first == _second)
                return 0;

            Vector2 _firstOffset = _first - _origin;
            Vector2 _secondOffset = _second - _origin;

            float _angle1 = Mathf.Atan2(_firstOffset.x, _firstOffset.y);
            float _angle2 = Mathf.Atan2(_secondOffset.x, _secondOffset.y);

            if (_angle1 < _angle2)
                return -1 * m_sortOrder;

            if (_angle1 > _angle2)
                return 1 * m_sortOrder;

            return (_firstOffset.sqrMagnitude < _secondOffset.sqrMagnitude) ? -1 * m_sortOrder : 1 * m_sortOrder;
        }
    }

    public void CreateConvexhull(List<Vector2> _points, int _size)
    {
        int _minX = 0, _maxX = 0;
        for (int i = 1; i < _size; i++)
        {
            if (_points[i].x < _points[_minX].x)
                _minX = i;
            if (_points[i].x > _points[_maxX].x)
                _maxX = i;
        }
        QuickHull(_points, _size, _points[_minX], _points[_maxX], 1);
        QuickHull(_points, _size, _points[_minX], _points[_maxX], -1);
    }

    private int FindSide(Vector2 _point1, Vector2 _point2, Vector2 _pointMaxDist)
    {
        int _val = LineDistance(_point1, _point2, _pointMaxDist);

        if (_val > 0)
            return 1;
        if (_val < 0)
            return -1;
        return 0;
    }
    private int LineDistance(Vector2 _point1, Vector2 _point2, Vector2 _pointMaxDist)
    {
        return (int)((_pointMaxDist.y - _point1.y) * (_point2.x - _point1.x) -
                   (_point2.y - _point1.y) * (_pointMaxDist.x - _point1.x));
    }

    private void QuickHull(List<Vector2> _points, int _size, Vector2 _point1, Vector2 _point2, int _side)
    {
        int _index = -1;
        int _maxDist = 0;
        int _temp;
        for (int i = 0; i < _size; i++)
        {
            _temp = Mathf.Abs(LineDistance(_point1, _point2, _points[i]));
            if (FindSide(_point1, _point2, _points[i]) == _side && _temp > _maxDist)
            {
                _index = i;
                _maxDist = _temp;
            }
        }

        if (_index == -1)
        {
            if (!m_convexhull.Contains(_point1)) m_convexhull.Add(_point1);
            if (!m_convexhull.Contains(_point2)) m_convexhull.Add(_point2);
            return;
        }
        QuickHull(_points, _size, _points[_index], _point1, -FindSide(_points[_index], _point1, _point2));
        QuickHull(_points, _size, _points[_index], _point2, -FindSide(_points[_index], _point2, _point1));
    }

    private void GenPoints()
    {
        float _rand1;
        float _rand2;
        for (int i = 0; i < m_pointDensity; i++)
        {
            _rand1 = UnityEngine.Random.Range(m_roadBounds.x, m_roadBounds.width);
            _rand2 = UnityEngine.Random.Range(m_roadBounds.y, m_roadBounds.height);
            m_randPoints.Add(new Vector2(_rand1, _rand2));
        }
    }

    public void GenerateSpline()
    {
        m_convexhull.Clear();
        m_randPoints.Clear();

        GenPoints();
        CreateConvexhull(m_randPoints, m_randPoints.Count);

        m_convexhull.Sort((new CurveHandler.ClockwiseComparer(new Vector2(0f, 0f), sortOrder)));

        splinePoints.Clear();

        GenerateTrackVariation();
        EvenlySpacedSpline();
    }

    private void EvenlySpacedSpline()
    {
        float _dstSinceLastEvenPoint = 0;
        float _netLenControl;
        float _estimatedCurveLength;
        float _t;
        float _extraDistance;
        int _divisions;
        Vector2 _previousPoint = m_convexhull[1];
        Vector2 _pointOnSpline;
        Vector2 _newPoint;
        Vector2[] _segment = new Vector2[4];

        for (int pos = 0; pos < m_convexhull.Count - 3; pos++)
        {
            _segment[0] = m_convexhull[pos];
            _segment[1] = m_convexhull[pos + 1];
            _segment[2] = m_convexhull[pos + 2];
            _segment[3] = m_convexhull[pos + 3];

            _netLenControl = Vector2.Distance(_segment[0], _segment[1]) + Vector2.Distance(_segment[1], _segment[2]) + Vector2.Distance(_segment[2], _segment[3]);
            _estimatedCurveLength = Vector2.Distance(_segment[0], _segment[3]) + _netLenControl / 2f;
            _divisions = Mathf.CeilToInt(_estimatedCurveLength * 10);
            _t = 0;

            while (_t <= 1)
            {
                _t += 1f / _divisions;
                _pointOnSpline = GetCatmullRomPosition(_t, _segment);
                _dstSinceLastEvenPoint += Vector2.Distance(_previousPoint, _pointOnSpline);
                while (_dstSinceLastEvenPoint >= m_detailLevel)
                {
                    _extraDistance = _dstSinceLastEvenPoint - m_detailLevel;
                    _newPoint = _pointOnSpline + (_previousPoint - _pointOnSpline).normalized * _extraDistance;
                    splinePoints.Add(_newPoint);
                    _dstSinceLastEvenPoint = _extraDistance;
                    _previousPoint = _newPoint;
                }

                _previousPoint = _pointOnSpline;
            }
        }
    }

    private void GenerateTrackVariation()
    {

        for (int i = 0; i < 4; i++)
        {
            PushApart();
        }

        DisplacePoints();

        for (int i = 0; i < 5; i++)
        {
            PushApart();
        }

        for (int i = 0; i < 10; i++)
        {
            FixAngles();
        }

        for (int i = 0; i < 5; i++)
        {
            PushApart();
        }
        m_convexhull.Sort((new CurveHandler.ClockwiseComparer(new Vector2(0f, 0f), sortOrder)));
    }
    private void FixAngles()
    {

        Vector2 _prevVec;
        Vector2 _nextVec;
        float _angle;
        float _nextVecLen;
        float _nAngle;
        float _diff;
        float _cos;
        float _sin;
        float _newX;
        float _newY;
        for (int i = 0; i < m_convexhull.Count; i++)
        {
            int _previousPoint = ((i - 1 + m_convexhull.Count) % m_convexhull.Count);
            int _nextPoint = (i + 1) % m_convexhull.Count;
            _prevVec = (m_convexhull[i] - m_convexhull[_previousPoint]).normalized;
            _nextVec = (m_convexhull[_nextPoint] - m_convexhull[i]);
            _nextVecLen = _nextVec.magnitude;
            _nextVec.Normalize();
            _angle = Mathf.Atan2(_prevVec.x * _nextVec.y - _prevVec.y * _nextVec.x, _prevVec.x * _nextVec.x + _prevVec.y * _nextVec.y);

            if (Mathf.Abs(_angle * Mathf.Rad2Deg) <= 100)
            {
                continue;
            }
            _nAngle = 100 * Mathf.Sign(_angle) * Mathf.Deg2Rad;
            _diff = _nAngle - _angle;
            _cos = (float)Mathf.Cos(_diff);
            _sin = (float)Mathf.Sin(_diff);
            _newX = _nextVec.x * _cos - _nextVec.y * _sin;
            _newY = _nextVec.x * _sin + _nextVec.y * _cos;
            _newX *= _nextVecLen;
            _newY *= _nextVecLen;
            m_convexhull[_nextPoint] = m_convexhull[i] + new Vector2(_newX, _newY);
        }
    }
    private void DisplacePoints()
    {
        Vector2 _displacement;
        Vector2 _normal;
        List<Vector2> _rSet = new List<Vector2>();
        const float _maxDisp = 20f;

        for (int i = 0; i < m_convexhull.Count; i++)
        {
            float _dispLen = Mathf.Pow(m_difficulty, UnityEngine.Random.value) * _maxDisp;
            _normal = (m_convexhull[(i + 1) % m_convexhull.Count] - m_convexhull[i]).normalized;
            _displacement = new Vector2(-_normal.y, _normal.x);
            _displacement *= _dispLen * sortOrder;

            _rSet.Add(new Vector2(m_convexhull[i].x, m_convexhull[i].y));

            _rSet.Add(((m_convexhull[i] + m_convexhull[(i + 1) % m_convexhull.Count]) / 2f) + _displacement);
        }

        m_convexhull = new List<Vector2>(_rSet);
    }

    Vector2 GetCatmullRomPosition(float _t, Vector2[] _points)
    {
        return (0.5f * (2f * _points[1]))
                + ((0.5f * (_points[2] - _points[0])) * _t)
                + ((0.5f * (2f * _points[0] - 5f * _points[1] + 4f * _points[2] - _points[3])) * _t * _t)
                + ((0.5f * (-_points[0] + 3f * _points[1] - 3f * _points[2] + _points[3])) * _t * _t * _t);
    }

    public Vector3 GetCatmullRomTangent(float t, Vector3[] _points)
    {
        return 1.5f * (t * t) * (-_points[0] + 3 * _points[1] - 3 * _points[2] + _points[3]) + t * (2 * _points[0] - 5 * _points[1] + 4 * _points[2] - _points[3]) + 0.5f * (_points[2] - _points[0]);
    }

    private void PushApart()
    {
        const float _mindist = 80f;
        float _dist;
        float _difference;
        Vector2 _vec;

        for (int i = 0; i < m_convexhull.Count - 1; i++)
        {

            for (int j = i + 1; j < m_convexhull.Count; j++)
            {
                if (Vector2.Distance(m_convexhull[i], m_convexhull[j]) < _mindist)
                {
                    _vec = m_convexhull[j] - m_convexhull[i];
                    _dist = _vec.magnitude;
                    _vec /= _dist;
                    _difference = _mindist - _dist;
                    _vec *= (_difference / 2f);

                    m_convexhull[j] += _vec;
                    m_convexhull[i] -= _vec;
                }

            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        int j = 0;
        foreach (var i in m_convexhull)
        {
            Gizmos.DrawWireSphere(new Vector3(i.x, 0f, i.y), 5f);
            j++;
        }
    }
}

