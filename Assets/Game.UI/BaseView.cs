using System;
using System.Reflection;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 视图基类：反射自动绑定 UI（字段名 == 子节点名），减少手动拖拽。
    /// 与旧客户端 BaseView 行为一致，保证场景替换后引用可自动恢复。
    /// </summary>
    public abstract class BaseView : MonoBehaviour
    {
        private bool _isInitialized;

        [Header("VFX")]
        public GameObject VfxPrefab;

        protected virtual void Awake()
        {
            if (!_isInitialized)
            {
                AutoBindUI();
                _isInitialized = true;
            }
        }

        public void AutoBindUI()
        {
            Type type = GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (FieldInfo field in fields)
            {
                Type fieldType = field.FieldType;
                bool isGameObject = fieldType == typeof(GameObject);
                bool isComponent = typeof(Component).IsAssignableFrom(fieldType);

                if (!isGameObject && !isComponent)
                {
                    continue;
                }

                // Inspector 手动绑定优先
                if (field.GetValue(this) as UnityEngine.Object != null)
                {
                    continue;
                }

                Transform target = FindChildRecursive(transform, field.Name);
                if (target == null)
                {
                    continue;
                }

                if (isGameObject)
                {
                    field.SetValue(this, target.gameObject);
                }
                else
                {
                    Component component = target.GetComponent(fieldType);
                    if (component != null)
                    {
                        field.SetValue(this, component);
                    }
                }
            }
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            Transform result = parent.Find(name);
            if (result != null)
            {
                return result;
            }

            foreach (Transform child in parent)
            {
                if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            foreach (Transform child in parent)
            {
                result = FindChildRecursive(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>播放粒子特效（1 秒后自动销毁防泄漏）。</summary>
        public void PlayParticleEffect(Vector3 pos, Color color)
        {
            if (VfxPrefab == null)
            {
                return;
            }

            GameObject vfx = Instantiate(VfxPrefab, pos, Quaternion.identity);
            var main = vfx.GetComponent<ParticleSystem>().main;
            main.startColor = color;
            Destroy(vfx, 1.0f);
        }
    }
}
