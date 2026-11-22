// 在服务端管理玩家血量并同步血量变化
using System;
using Mirror;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
{
    // 玩家满血时的生命值
    [SerializeField] int maxhealth=100;
    // 由服务端同步的当前生命值
    [SyncVar(hook =nameof(OnHealthChanged))] int health;

    public int MaxHealth=>maxhealth;
    public int Health=>health;

    // 血量变化时通知订阅方
    public event Action<int,int>HealthChanged;

    // 在服务端生成时将血量设为最大值
    public override void OnStartServer()
    {
        health=maxhealth;
    }

    // 在服务端结算受击伤害
    public void ServerTakeDemage(int amount,uint attackerNetId)
    {
        // 校验服务端状态和伤害数值
        if(!isServer||amount<0)return;
        // 扣除血量并记录伤害来源，最低扣到零
        health=Math.Max(0,health-amount);
        Debug.Log($"[Combat] netId={netId} 受击 {amount}，剩余 {health}（来自 netId={attackerNetId}）");
    }

    // 通知订阅方血量变化前后的数值
    void OnHealthChanged(int oldValue,int newValue)
    {
        HealthChanged?.Invoke(oldValue,newValue);
    }
}

