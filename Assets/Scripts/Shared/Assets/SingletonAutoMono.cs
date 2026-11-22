// 按需创建可跨场景使用的单例组件
using UnityEngine;

public class SingletonAutoMono<T> : MonoBehaviour where T : MonoBehaviour
{
    // 缓存当前组件类型的唯一实例
    private static T instance;
    // 获取单例实例，首次调用时自动创建
    public static T GetInstance()
    {
        // 创建并保留跨场景使用的对象
        if( instance == null )
        {
            GameObject obj = new GameObject(typeof(T).ToString());
            DontDestroyOnLoad(obj);
            instance = obj.AddComponent<T>();
        }
        return instance;
    }

}
