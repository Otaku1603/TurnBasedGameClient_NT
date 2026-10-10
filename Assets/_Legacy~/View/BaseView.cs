using System;
using System.Reflection;
using UnityEngine;

namespace TurnBasedGame.View
{
    /// <summary>
    /// 视图基类，提供UI自动绑定和特效播放功能
    /// 通过反射自动查找并绑定UI组件，减少手动拖拽
    /// </summary>
    public abstract class BaseView : MonoBehaviour
    {
        private bool _isInitialized = false;

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

        /// <summary>
        /// 自动绑定 UI 组件
        /// 这里用反射遍历字段，只要字段名和 Unity 里的节点名一样，就自动找出来赋值。
        /// </summary>
        public void AutoBindUI()
        {
            Type type = this.GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (FieldInfo field in fields)
            {
                Type fieldType = field.FieldType;
                bool isGameObject = fieldType == typeof(GameObject);
                bool isComponent = typeof(Component).IsAssignableFrom(fieldType);

                if (!isGameObject && !isComponent) continue;

                // 如果在 Inspector 里手动绑定这个组件，就以手动为准，跳过自动查找
                var currentValue = field.GetValue(this) as UnityEngine.Object;
                if (currentValue != null)
                {
                    continue;
                }

                // 自动查找逻辑 (作为兜底)
                string nodeName = field.Name;
                Transform targetTransform = FindChildRecursive(this.transform, nodeName);

                if (targetTransform != null)
                {
                    if (isGameObject) field.SetValue(this, targetTransform.gameObject);
                    else
                    {
                        Component component = targetTransform.GetComponent(fieldType);
                        if (component != null) field.SetValue(this, component);
                    }
                }
            }
        }

        /// <summary>
        /// 递归查找子节点，支持多层嵌套的UI结构
        /// </summary>
        private Transform FindChildRecursive(Transform parent, string name)
        {
            Transform result = parent.Find(name);
            if (result != null) return result;
            foreach (Transform child in parent)
            {
                if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return child;
            }
            foreach (Transform child in parent)
            {
                result = FindChildRecursive(child, name);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// 播放粒子特效，支持颜色自定义
        /// 特效1秒后自动销毁，防止内存泄漏
        /// </summary>
        public void PlayParticleEffect(Vector3 pos, Color color)
        {
            if (VfxPrefab == null) return;

            // 1. 实例化
            GameObject vfx = Instantiate(VfxPrefab, pos, Quaternion.identity);

            // 2. 改颜色
            var main = vfx.GetComponent<ParticleSystem>().main;
            main.startColor = color;

            // 3. 自动销毁 (1秒后)
            Destroy(vfx, 1.0f);
        }
    }
}