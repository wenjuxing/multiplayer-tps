// 定义武器伤害、开火间隔和射程的默认值
using UnityEngine;
using System;

[Serializable]
public class WeaponStats
{
    // 每发子弹对玩家造成的伤害
    public int demage=25;
    // 两次开火之间的最短时间（秒）
    public float fireInterval=0.2f;
    // 枪口到射击终点的最大距离
    [Min(0.1f)] public float range = 30f;

}

