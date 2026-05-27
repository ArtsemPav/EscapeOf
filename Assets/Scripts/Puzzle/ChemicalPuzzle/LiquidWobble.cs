using UnityEngine;

namespace ChemicalPuzzle
{
    /// <summary>
    /// Управляет жидкостью в колбе.
    ///
    /// Dual-plane подход:
    ///   _LocalFillY  — порог в локальном (mesh) пространстве.
    ///                  Clip по локальному Y сохраняет РЕАЛЬНЫЙ 3D-объём при любом наклоне,
    ///                  т.к. для произвольной формы меша локальный Y-срез — это всегда
    ///                  та же доля геометрии, независимо от поворота объекта в мире.
    ///   _FillWorldY  — мировой Y для foam-полосы.
    ///                  Даёт визуально горизонтальную линию поверхности.
    ///                  Рассчитывается через TransformPoint центральной точки заполнения.
    /// </summary>
    [ExecuteAlways]
    public class LiquidWobble : MonoBehaviour
    {
        [Header("Fill")]
        [Tooltip("Доля заполнения: 0 = пусто, 1 = полная.")]
        [Range(0f, 1f)]
        public float fillFraction = 0.5f;

        [Tooltip("Скорость плавного следования foam-линии за целевым мировым Y.")]
        [SerializeField] private float correctionSpeed = 15f;

        [Header("Wobble")]
        [Tooltip("Максимальная амплитуда покачивания.")]
        [SerializeField] private float maxWobble   = 0.03f;
        [SerializeField] private float wobbleSpeed = 4f;
        [SerializeField] private float recovery    = 1.5f;

        private float      _fillWorldY;
        private bool       _initialized;
        private Renderer   _renderer;
        private MeshFilter _meshFilter;
        private Material   _material;
        private Vector3    _lastPos;
        private Quaternion _lastRot;
        private float      _wobbleAddX, _wobbleAddZ, _wobbleX, _wobbleZ;
        private float      _time;

        // Локальные границы меша — кэшируются один раз, не зависят от трансформа
        private float _localMeshMin;
        private float _localMeshHeight;

        private static readonly int FillAmountId   = Shader.PropertyToID("_FillAmount");
        private static readonly int LocalMeshMinId = Shader.PropertyToID("_LocalMeshMin");
        private static readonly int LocalMeshMaxId = Shader.PropertyToID("_LocalMeshMax");
        private static readonly int PivotWSId      = Shader.PropertyToID("_PivotWS");
        private static readonly int WobbleXId      = Shader.PropertyToID("_WobbleX");
        private static readonly int WobbleZId      = Shader.PropertyToID("_WobbleZ");

        private MaterialPropertyBlock _propBlock;

        private void OnEnable()
        {
            _renderer   = GetComponent<Renderer>();
            _meshFilter = GetComponent<MeshFilter>();
            _propBlock  = new MaterialPropertyBlock();
            _lastPos    = transform.position;
            _lastRot    = transform.rotation;
            _initialized = false;

            CacheLocalMeshBounds();
        }

        private void CacheLocalMeshBounds()
        {
            if (_meshFilter != null && _meshFilter.sharedMesh != null)
            {
                Bounds b         = _meshFilter.sharedMesh.bounds;
                _localMeshMin    = b.min.y;
                _localMeshHeight = b.size.y;
            }
            else
            {
                _localMeshMin    = -0.5f;
                _localMeshHeight = 1.0f;
            }
        }

        private void Update()
        {
            if (_renderer == null) return;

            // В редакторе (не в игре) позволяем менять значение через материал
            #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                _material = _renderer.sharedMaterial;
                if (_material != null && _material.HasProperty(FillAmountId))
                {
                    if (!UnityEditor.AnimationMode.InAnimationMode())
                    {
                        float matFill = _material.GetFloat(FillAmountId);
                        if (!Mathf.Approximately(matFill, fillFraction))
                        {
                            fillFraction = matFill;
                        }
                    }
                }
            }
            #endif

            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            if (dt <= 0) dt = 0.016f;

            if (Application.isPlaying)
            {
                _time += dt;
                
                _wobbleAddX = Mathf.Lerp(_wobbleAddX, 0f, dt * recovery);
                _wobbleAddZ = Mathf.Lerp(_wobbleAddZ, 0f, dt * recovery);

                float pulse = 2f * Mathf.PI * wobbleSpeed;
                _wobbleX = _wobbleAddX * Mathf.Sin(pulse * _time);
                _wobbleZ = _wobbleAddZ * Mathf.Sin(pulse * _time);

                Vector3 velocity = (transform.position - _lastPos) / dt;
                Quaternion deltaRot = transform.rotation * Quaternion.Inverse(_lastRot);
                deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180) angle -= 360;
                Vector3 angularVelocity = axis * (angle * Mathf.Deg2Rad / dt);

                _wobbleAddX += Mathf.Clamp((velocity.x + angularVelocity.z * 0.2f) * maxWobble, -maxWobble, maxWobble) * dt * 5f;
                _wobbleAddZ += Mathf.Clamp((velocity.z + angularVelocity.x * 0.2f) * maxWobble, -maxWobble, maxWobble) * dt * 5f;

                _lastPos = transform.position;
                _lastRot = transform.rotation;
            }

            // Sync with shader properties using PropertyBlock
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(FillAmountId, fillFraction);
            _propBlock.SetFloat(LocalMeshMinId, _localMeshMin);
            _propBlock.SetFloat(LocalMeshMaxId, _localMeshMin + _localMeshHeight);
            _propBlock.SetVector(PivotWSId, transform.position);
            _propBlock.SetFloat(WobbleXId, _wobbleX);
            _propBlock.SetFloat(WobbleZId, _wobbleZ);
            _renderer.SetPropertyBlock(_propBlock);

        #if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.SceneView.RepaintAll();
        #endif
        }

        private void OnDisable()
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(WobbleXId, 0f);
            _propBlock.SetFloat(WobbleZId, 0f);
            _renderer.SetPropertyBlock(_propBlock);
        }
    }
}
