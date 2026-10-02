using Mirror;
using UnityEngine;

namespace QuickStart
{
    public class PlayerScript : NetworkBehaviour
    {
        public TextMesh playerNameText;
        public GameObject floatingInfo;
        
        private Material playerMaterialClone;
        
        //网络同步属性
        [SyncVar(hook =nameof(OnNameChange))]
        public string playerName;
        [SyncVar(hook =nameof(OnColorChange))]
        public Color playerColor=Color.white;

        void OnNameChange(string _old,string _new)
        {
            playerNameText.text=playerName;
        }
        void OnColorChange(Color _old,Color _new)
        {
            //修改名字颜色
            playerNameText.color=_new;
            
            //修改材质颜色
            playerMaterialClone=new Material(GetComponent<Renderer>().material);
            playerMaterialClone.color=_new;
            GetComponent<Renderer>().material=playerMaterialClone;
        }
        // 在服务器生成时赋值，替代原来的 CmdSetupPlayer
        public override void OnStartServer()
        {
            playerName = "Player" + Random.Range(100, 999);
            playerColor = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f));
        }
        //进入游戏时调用
        public override void OnStartLocalPlayer()
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0, 0, 0)+new Vector3(0,1.25f,-4.5f);

            transform.position+=Vector3.up;           
        }

        void Update()
        {
            if (!isLocalPlayer) 
            {
                floatingInfo.transform.LookAt(Camera.main.transform); 
                return; 
            }

            float moveX = Input.GetAxis("Horizontal") * Time.deltaTime * 110.0f;
            float moveZ = Input.GetAxis("Vertical") * Time.deltaTime * 4f;

            transform.Rotate(0, moveX, 0);
            transform.Translate(0, 0, moveZ);
        }
    }
}