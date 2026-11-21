using System;
using Mirror;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
{
    [SerializeField] int maxhealth=100;
    [SyncVar(hook =nameof(OnHealthChanged))] int health;

    public int MaxHealth=>maxhealth;
    public int Health=>health;

    public event Action<int,int>HealthChanged;

    //服务端加载时调用
    public override void OnStartServer()
    {
        health=maxhealth;
    }

    //仅在服务端造成伤害
    public void ServerTakeDemage(int amount,uint attackerNetId)
    {
        if(!isServer||amount<0)return;
        health=Math.Max(0,health-amount);
        Debug.Log($"[Combat] netId={netId} 受击 {amount}，剩余 {health}（来自 netId={attackerNetId}）");
    }

    void OnHealthChanged(int oldValue,int newValue)
    {
        HealthChanged?.Invoke(oldValue,newValue);
    }
}

