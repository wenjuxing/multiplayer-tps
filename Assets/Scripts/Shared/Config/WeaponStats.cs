using UnityEngine;
using System;

[Serializable]
public class WeaponStats
{
    public int demage=25;
    public float fireInterval=0.2f;
    [Min(0.1f)] public float range = 30f;

}

