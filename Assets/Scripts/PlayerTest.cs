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
            localCamera = Camera.main;
            if (localCamera != null)
            {
                localCamera.transform.SetParent(transform);
                localCamera.transform.localPosition = new Vector3(0f, 1.25f, -4.5f);
            }
            else
            {
                Debug.LogWarning("[PlayerScript] 找不到 tag=MainCamera 的相机，跳过相机跟随。");
            }

            transform.position+=Vector3.up;           
        }

        // 本地玩家离开（断线/换场景）时把相机还给场景，
        // 否则相机会作为玩家的子物体被一起销毁，下次进游戏就没有主相机了。
        public override void OnStopLocalPlayer()
        {
            if (localCamera == null) return;

            localCamera.transform.SetParent(null, true);
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

            // ---- 本地玩家：目前仍是客户端权威，P1 再改成 [Command] 上报输入 ----
            float moveX = Input.GetAxis("Horizontal") * Time.deltaTime * 110.0f;
            float moveZ = Input.GetAxis("Vertical") * Time.deltaTime * 4f;

            transform.Rotate(0, moveX, 0);
            transform.Translate(0, 0, moveZ);
        }
    }
}
