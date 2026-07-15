using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AIBridge.Agent
{
    public static class GameUtility
    {
        #region 一、游戏对象与组件操作 (GameObject & Component)

        [AgentCommand("尝试获取物体上的指定组件，如果不存在则自动挂载并返回。", category: "GameObject")]
        public static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            if (gameObject == null)
                return null;

            T component = gameObject.GetComponent<T>();
            if (component == null)
                component = gameObject.AddComponent<T>();
            return component;
        }

        [AgentCommand("遍历并销毁父节点下的所有子物体，常用于清空列表UI或重置挂点。", category: "GameObject")]
        public static void DestroyAllChildren(Transform parent)
        {
            if (parent == null)
                return;

            // 从后往前删除，避免 childCount 变化导致索引错误
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                else
#endif
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        [AgentCommand("递归深度查找并返回指定名称的子物体，打破 Unity 原生 Find 只能查找直接子物体的限制。", category: "GameObject")]
        public static Transform FindChildRecursively(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrEmpty(childName))
                return null;

            // 先检查直接子物体
            Transform direct = parent.Find(childName);
            if (direct != null)
                return direct;

            // 递归检查所有子物体
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform result = FindChildRecursively(parent.GetChild(i), childName);
                if (result != null)
                    return result;
            }

            return null;
        }

        [AgentCommand("将指定物体及其所有子物体的 Layer 统一修改为新的层级。", category: "GameObject")]
        public static void SetLayerRecursively(GameObject obj, int newLayer)
        {
            if (obj == null)
                return;

            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                if (child != null)
                    SetLayerRecursively(child.gameObject, newLayer);
            }
        }

        #endregion

        #region 二、变换与坐标空间 (Transform & Spatial)

        [AgentCommand("将目标的本地坐标归零，旋转设为无旋转，缩放设为1。", category: "Transform")]
        public static void ResetTransform(Transform target)
        {
            if (target == null)
                return;

            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;
            target.localScale = Vector3.one;
        }

        [AgentCommand("专为2D游戏设计的LookAt方法，计算角度并修改Z轴旋转，使物体朝向目标点。", category: "Transform")]
        public static void LookAt2D(Transform transform, Vector2 targetPosition)
        {
            if (transform == null)
                return;

            Vector2 direction = targetPosition - (Vector2)transform.position;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        [AgentCommand("将3D世界坐标转换为UI Canvas下的本地坐标，常用于角色头顶血条或跟随UI。", category: "Transform")]
        public static Vector2 WorldToCanvasPosition(Canvas canvas, Camera camera, Vector3 worldPosition)
        {
            if (canvas == null)
                return Vector2.zero;

            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            Vector3 screenPoint = camera != null ? camera.WorldToScreenPoint(worldPosition) : worldPosition;

            // 对于 Overlay Canvas，不需要指定相机
            Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, eventCamera, out Vector2 localPoint);
            return localPoint;
        }

        [AgentCommand("通过计算包围盒与视椎体的相交情况，判断物体当前是否在指定相机的视野范围内。", category: "Transform")]
        public static bool IsVisibleFrom(Renderer renderer, Camera camera)
        {
            if (renderer == null || camera == null)
                return false;

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
        }

        #endregion

        #region 三、数值与数学逻辑 (Math & Logic)

        [AgentCommand("线性映射，将值从一个区间按比例映射到另一个区间。", category: "Math")]
        public static float Remap(float value, float from1, float to1, float from2, float to2)
        {
            if (Mathf.Approximately(from1, to1))
                return from2; // 避免除零，返回目标区间起点

            float t = (value - from1) / (to1 - from1);
            return from2 + t * (to2 - from2);
        }

        [AgentCommand("判断两个浮点数是否近似相等，避免直接比较的精度问题。", category: "Math")]
        public static bool IsApproximate(float a, float b, float tolerance = 0.01f)
        {
            return Mathf.Abs(a - b) <= tolerance;
        }

        [AgentCommand("二次贝塞尔曲线，根据时间t计算路径上的点坐标。", category: "Math")]
        public static Vector3 GetBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
        {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        #endregion

        #region 四、随机与概率 (Random & Probability)

        [AgentCommand("从列表中随机抽取一个元素。", category: "Random")]
        public static T GetRandomElement<T>(IList<T> list)
        {
            if (list == null || list.Count == 0)
                return default;

            int index = UnityEngine.Random.Range(0, list.Count);
            return list[index];
        }

        [AgentCommand("基于权重数组的随机抽取，返回被抽中项的索引。", category: "Random")]
        public static int GetRandomIndexByWeight(float[] weights)
        {
            if (weights == null || weights.Length == 0)
                return -1;

            float totalWeight = 0f;
            foreach (float w in weights)
            {
                if (w > 0f)
                    totalWeight += w;
            }

            // 所有权重都为0或负数时，无法加权抽取，返回 -1
            if (totalWeight <= 0f)
                return -1;

            float randomValue = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] > 0f)
                {
                    cumulative += weights[i];
                    if (randomValue <= cumulative)
                        return i;
                }
            }

            // 因为浮点精度，保险返回最后一个有效权重的索引
            for (int i = weights.Length - 1; i >= 0; i--)
            {
                if (weights[i] > 0f)
                    return i;
            }
            return -1;
        }

        [AgentCommand("按给定概率返回是否触发成功。", category: "Random")]
        public static bool Chance(float probability)
        {
            return UnityEngine.Random.value <= Mathf.Clamp01(probability);
        }

        [AgentCommand("在指定的包围盒区域内随机生成一个坐标点。", category: "Random")]
        public static Vector3 GetRandomPointInBounds(Bounds bounds)
        {
            float x = UnityEngine.Random.Range(bounds.min.x, bounds.max.x);
            float y = UnityEngine.Random.Range(bounds.min.y, bounds.max.y);
            float z = UnityEngine.Random.Range(bounds.min.z, bounds.max.z);
            return new Vector3(x, y, z);
        }

        #endregion

        #region 五、物理与射线检测 (Physics & Raycast)

        [AgentCommand("从高空向下射线检测地面，成功则返回 true 并输出地表坐标。", category: "Physics")]
        public static bool TryGetGroundPosition(Vector3 startPos, out Vector3 groundPos, LayerMask groundLayer)
        {
            RaycastHit hit;
            if (Physics.Raycast(startPos, Vector3.down, out hit, Mathf.Infinity, groundLayer))
            {
                groundPos = hit.point;
                return true;
            }

            groundPos = Vector3.zero;
            return false;
        }

        [AgentCommand("将屏幕鼠标指针的2D坐标转换为击中地面的3D世界坐标。", category: "Physics")]
        public static Vector3 GetMouseWorldPosition(Camera camera, LayerMask hitLayer)
        {
            if (camera == null)
                return Vector3.zero;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, hitLayer))
            {
                return hit.point;
            }

            return Vector3.zero;
        }

        #endregion

        #region 六、时间与异步流 (Time & Async)

        [AgentCommand("将纯数字秒数格式化为 \"MM:SS\" 或 \"HH:MM:SS\" 格式的字符串。", category: "Time")]
        public static string FormatTimeSeconds(float timeInSeconds, bool showHours)
        {
            if (timeInSeconds < 0f)
                timeInSeconds = 0f;

            TimeSpan timeSpan = TimeSpan.FromSeconds(timeInSeconds);
            if (showHours)
                return string.Format("{0:D2}:{1:D2}:{2:D2}", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
            else
                return string.Format("{0:D2}:{1:D2}", timeSpan.Minutes, timeSpan.Seconds);
        }

        [AgentCommand("极简定时器协程，等待指定秒数后自动执行回调。", category: "Time")]
        public static IEnumerator DelayExecute(float delayTime, Action callback)
        {
            yield return new WaitForSeconds(delayTime);
            callback?.Invoke();
        }

        [AgentCommand("延迟指定帧数后执行回调，常用于避开生命周期冲突。", category: "Time")]
        public static IEnumerator WaitFrames(int frameCount, Action callback)
        {
            for (int i = 0; i < frameCount; i++)
                yield return null;

            callback?.Invoke();
        }

        #endregion

        #region 七、UI 与 字符串处理 (UI & String)

        [AgentCommand("封装化设置 UI 组的可见与射线拦截状态，性能优于 SetActive 且支持动画。", category: "UI")]
        public static void SetCanvasGroupVisible(CanvasGroup canvasGroup, bool isVisible)
        {
            if (canvasGroup == null)
                return;

            canvasGroup.alpha = isVisible ? 1f : 0f;
            canvasGroup.blocksRaycasts = isVisible;
        }

        [AgentCommand("强制 LayoutGroup 立刻刷新排版，解决动态添加元素后布局错乱问题。", category: "UI")]
        public static void RebuildLayoutNow(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        [AgentCommand("将 Color 对象转换为 \"#RRGGBB\" 格式的十六进制字符串。", category: "UI")]
        public static string ColorToHex(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        [AgentCommand("将 \"#RRGGBB\" 或 \"#RRGGBBAA\" 格式的十六进制字符串转换为 Color 对象。", category: "UI")]
        public static Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return Color.white;

            if (!hex.StartsWith("#"))
                hex = "#" + hex;

            if (ColorUtility.TryParseHtmlString(hex, out Color color))
                return color;

            return Color.white; // 解析失败返回默认白色
        }

        #endregion

        #region 八、数据持久化与存储 (IO & Data)

        [AgentCommand("将泛型数据序列化为 JSON 并写入持久化目录。", category: "Data")]
        public static void SaveJsonData<T>(string fileName, T data)
        {
            string path = Path.Combine(Application.persistentDataPath, fileName);
            string json = SerializeToJson(data);
            File.WriteAllText(path, json);
        }

        [AgentCommand("从持久化目录读取 JSON 并反序列化回数据对象，文件不存在则返回新实例。", category: "Data")]
        public static T LoadJsonData<T>(string fileName)
        {
            string path = Path.Combine(Application.persistentDataPath, fileName);
            if (!File.Exists(path))
                return default(T);

            string json = File.ReadAllText(path);
            return DeserializeFromJson<T>(json);
        }

        // ------------------------ JsonUtility 列表/数组包装器 ------------------------
        [Serializable]
        private class Wrapper<TItem>
        {
            public TItem[] Items;
        }

        [AgentCommand("内部序列化，自动处理 List、数组等 JsonUtility 不直接支持的类型。", category: "Data")]
        private static string SerializeToJson<T>(T data)
        {
            if (data == null)
                return "{}";

            Type type = typeof(T);
            // 如果 T 是 List<>，包装为数组再序列化
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type elementType = type.GetGenericArguments()[0];
                IList list = data as IList;
                if (list == null)
                    return "{}";

                Array array = Array.CreateInstance(elementType, list.Count);
                list.CopyTo(array, 0);

                Type wrapperType = typeof(Wrapper<>).MakeGenericType(elementType);
                object wrapper = Activator.CreateInstance(wrapperType);
                var field = wrapperType.GetField("Items");
                field.SetValue(wrapper, array);

                return JsonUtility.ToJson(wrapper, true);
            }

            return JsonUtility.ToJson(data, true);
        }

        [AgentCommand("内部反序列化，自动处理 List、数组等 JsonUtility 不直接支持的类型。", category: "Data")]
        private static T DeserializeFromJson<T>(string json)
        {
            Type type = typeof(T);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type elementType = type.GetGenericArguments()[0];
                Type wrapperType = typeof(Wrapper<>).MakeGenericType(elementType);
                object wrapper = JsonUtility.FromJson(json, wrapperType);
                var field = wrapperType.GetField("Items");
                Array array = field.GetValue(wrapper) as Array;
                if (array == null)
                    return default(T);

                IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                foreach (object item in array)
                    list.Add(item);

                return (T)list;
            }

            return JsonUtility.FromJson<T>(json);
        }

        #endregion
    }
}