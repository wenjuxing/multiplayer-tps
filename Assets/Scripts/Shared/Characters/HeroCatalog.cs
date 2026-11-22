// 保存角色外观配置并按角色 ID 查找
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class HeroDefinition
{
    // 用于查找和同步外观的角色 ID
    [SerializeField] string roleId;
    // 角色外观预制体
    [SerializeField] GameObject visualPrefab;
    // 角色动画使用的骨骼映射
    [SerializeField] Avatar avatar;

    public string RoleId => roleId;
    public GameObject VisualPrefab => visualPrefab;
    public Avatar Avatar => avatar;
}

[CreateAssetMenu(menuName = "FPSGame/Hero Catalog", fileName = "HeroCatalog")]
public sealed class HeroCatalog : ScriptableObject
{
    // 可供角色装配的外观配置
    [SerializeField] List<HeroDefinition> heroes = new List<HeroDefinition>();

    // 按角色 ID 查找对应配置
    public bool TryGetHero(string roleId, out HeroDefinition definition) {
        definition = null;
        if (string.IsNullOrWhiteSpace(roleId)) {
            return false;
        }

        // 按目录顺序返回第一个精确匹配的角色
        foreach (HeroDefinition candidate in heroes) {
            if (candidate != null && string.Equals(candidate.RoleId, roleId, StringComparison.Ordinal)) {
                definition = candidate;
                return true;
            }
        }

        return false;
    }
}
