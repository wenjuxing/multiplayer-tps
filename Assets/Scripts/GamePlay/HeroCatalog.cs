using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class HeroDefinition
{
    [SerializeField] string roleId;
    [SerializeField] GameObject visualPrefab;
    [SerializeField] Avatar avatar;

    public string RoleId => roleId;
    public GameObject VisualPrefab => visualPrefab;
    public Avatar Avatar => avatar;
}

[CreateAssetMenu(menuName = "FPSGame/Hero Catalog", fileName = "HeroCatalog")]
public sealed class HeroCatalog : ScriptableObject
{
    [SerializeField] List<HeroDefinition> heroes = new List<HeroDefinition>();

    public bool TryGetHero(string roleId, out HeroDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(roleId))
        {
            for (int i = 0; i < heroes.Count; i++)
            {
                HeroDefinition candidate = heroes[i];
                if (candidate != null && string.Equals(candidate.RoleId, roleId, StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }
        }

        definition = null;
        return false;
    }
}
