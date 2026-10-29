using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// A small pool of clones of one inactive template, for presentation that
    /// shows a varying number of the same thing (ring walls, gap wedges, lob
    /// markers, orbit lines). Clones are only created when the pool runs dry, so
    /// steady-state play allocates nothing.
    ///
    /// Plain C#, built by its owner in <c>OnEnable</c> (never kept in a readonly
    /// field, CodingStandards §4): after a script reload the owner builds a new
    /// pool, and the constructor first removes the clones the old one left in
    /// the container.
    /// </summary>
    public sealed class ViewPool
    {
        readonly GameObject _template;
        readonly Transform _container;
        readonly List<GameObject> _free;
        readonly List<GameObject> _all;

        /// <param name="template">An inactive child to clone. Never shown itself.</param>
        /// <param name="container">Where clones live; emptied first.</param>
        /// <param name="prewarm">Clones made up front.</param>
        public ViewPool(GameObject template, Transform container, int prewarm)
        {
            _template = template;
            _container = container;
            _free = new List<GameObject>(Mathf.Max(4, prewarm));
            _all = new List<GameObject>(Mathf.Max(4, prewarm));
            ClearContainer(container);
            if (!IsValid) return;
            if (_template.activeSelf) _template.SetActive(false);
            for (int i = 0; i < prewarm; i++) _free.Add(Make());
        }

        /// <summary>True when the pool has a template and somewhere to put clones.</summary>
        public bool IsValid => _template != null && _container != null;

        /// <summary>A clone, made active. Null when the pool has no template.</summary>
        public GameObject Get()
        {
            if (!IsValid) return null;
            GameObject go = null;
            while (_free.Count > 0 && go == null)
            {
                go = _free[_free.Count - 1];
                _free.RemoveAt(_free.Count - 1);
            }
            if (go == null) go = Make();
            go.SetActive(true);
            return go;
        }

        /// <summary>Hide a clone and keep it for reuse.</summary>
        public void Release(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            if (!_free.Contains(go)) _free.Add(go);
        }

        /// <summary>Hide every clone.</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < _all.Count; i++) Release(_all[i]);
        }

        GameObject Make()
        {
            var go = Object.Instantiate(_template, _container, false);
            go.name = _template.name;
            go.SetActive(false);
            _all.Add(go);
            return go;
        }

        /// <summary>Destroy every child of <paramref name="container"/>: clones a
        /// previous pool (before a script reload) left behind.</summary>
        public static void ClearContainer(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i).gameObject;
                if (Application.isPlaying) Object.Destroy(child);
                else Object.DestroyImmediate(child);
            }
        }
    }
}
