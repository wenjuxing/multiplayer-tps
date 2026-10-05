using Mirror;
using UnityEngine;

namespace QuickStart
{
    public class PlayerScript : NetworkBehaviour
    {
        public TextMesh playerNameText;
        public GameObject floatingInfo;

        private Material playerMaterialClone;

        // 本地相机缓存。
        // Camera.main 在无头 Dedicated Server 上恒为 null（没有相机设备），
        // 旧代码在 Update 里对每个远端玩家每帧都取一次 Camera.main 且不判空，
        // 服务端上就是每帧一次 NullReferenceException。
        private static Camera localCamera;

        //网络同步属性
        [SyncVar(hook =nameof(OnNameChange))]
        public string playerName;
        [SyncVar(hook =nameof(OnColorChange))]
        public Color playerColor=Color.white;

        void OnNameChange(string _old,string _new)
        {
            if (playerNameText != null)
                playerNameText.text=playerName;
        }
        void OnColorChange(Color _old,Color _new)
        {
            //修改名字颜色
            if (playerNameText != null)
                playerNameText.color=_new;

            //修改材质颜色（无头服务端上 Renderer 仍存在，但这里只是表现层，判空更稳）
            Renderer r=GetComponent<Renderer>();
            if (r == null) return;

            playerMaterialClone=new Material(r.material);
            playerMaterialClone.color=_new;
            r.material=playerMaterialClone;
        }
        // 在服务器生成时赋值，替代原来的 CmdSetupPlayer
        public override void OnStartServer()
        {
            playerName = "Player" + Random.Range(100, 999);
            playerColor = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f));
        }
        //进入游戏时调用（只会在本地客户端执行，服务端不会走到这里）
        public override void OnStartLocalPlayer()
        {
            // 相机由角色身上的 CameraComp 自己驱动（它在 LateUpdate 里把 Main Camera 摆到角色身后），
            // 这里只缓存引用，供远端玩家名牌朝向用。
            localCamera = Camera.main;
            if (localCamera == null)
                Debug.LogWarning("[PlayerScript] 找不到 tag=MainCamera 的相机，名牌朝向会失效。");

            transform.position+=Vector3.up;           
        }

        // 本地玩家离开（断线/换场景）时清掉缓存；相机不在玩家身上，不会被连带销毁。
        public override void OnStopLocalPlayer()
        {
            localCamera = null;
        }

        void Update()
        {
            // 仅服务端（无头 Dedicated Server）：没有相机、没有本地输入，
            // 相机 / 名牌 / 输入逻辑一律不执行，从根上避免 Camera.main 空引用。
            if (isServerOnly) return;

            if (!isLocalPlayer) 
            {
                // 远端玩家的浮空名牌朝向本地相机
                if (localCamera == null) localCamera = Camera.main;
                if (localCamera != null && floatingInfo != null)
                    floatingInfo.transform.LookAt(localCamera.transform);
                return; 
            }

            
        }
    }
}
