using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace PersonalBossPowers
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class PersonalBossPowers : BaseUnityPlugin
    {
        private const string GUID = "PersonalBossPowers";
        private const string NAME = "PersonalBossPowers";
        private const string VERSION = "1.0.0";

        ServerSync.ConfigSync configSync = new ServerSync.ConfigSync(GUID) { DisplayName = GUID, CurrentVersion = VERSION, MinimumRequiredVersion = VERSION };
        ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);
            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
            return configEntry;
        }
        ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

        private static ConfigEntry<int> BossKillDistance;
        
        private void Awake()
        {
            BossKillDistance = config("General", "BossKillDistance", 40, "The distance in which the boss kill will be registered for the player");
            new Harmony(GUID).PatchAll();
        }
        
        [HarmonyPatch(typeof(BossStone),nameof(BossStone.DelayedAttachEffects_Step3))]
        private static class BossStone__Patch
        {
            [UsedImplicitly]
            private static bool Prefix(BossStone __instance)
            {
                if (__instance.m_activeEffect)
                {
                    __instance.m_activeEffect.SetActive(true);
                }
                __instance.m_activateStep3.Create(__instance.transform.position, __instance.transform.rotation, null, 1f, -1);
                __instance.StopCoroutine("FadeEmission");
                __instance.StartCoroutine("FadeEmission");
                return false;
            }
        }

        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.Interact))]
        private static class ItemStand_Interact_Patch
        {
            [UsedImplicitly]
            private static bool Prefix(ItemStand __instance)
            {
                if (!__instance.m_canBeRemoved && __instance.m_guardianPower is { } gp)
                {
                    var itemName = __instance.m_supportedItems[0].name;
                    if (!Player.m_localPlayer.m_customData.ContainsKey($"{itemName}_killed")) return false;
                    if (__instance.IsInvoking(nameof(ItemStand.DelayedPowerActivation)) || __instance.IsGuardianPowerActive(Player.m_localPlayer))
                        return false;

                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$guardianstone_hook_power_activate ");
                    __instance.m_activatePowerEffects.Create(__instance.transform.position, __instance.transform.rotation);
                    __instance.m_activatePowerEffectsPlayer.Create(Player.m_localPlayer.transform.position, Quaternion.identity, Player.m_localPlayer.transform);
                    __instance.Invoke(nameof(ItemStand.DelayedPowerActivation), __instance.m_powerActivationDelay);
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UpdateVisual))]
        private static class ItemStand_UpdateVisual_Patch
        {
            [UsedImplicitly]
            private static bool Prefix(ItemStand __instance)
            {
                if (!__instance.m_nview || !__instance.m_nview.IsValid())
                    return false;
                if (__instance.m_canBeRemoved || __instance.m_guardianPower is not { } gp) return true;
                if (!Player.m_localPlayer) return false;
                __instance.SetVisualItem(Player.m_localPlayer.m_customData.ContainsKey($"{__instance.m_supportedItems[0].name}_killed") ? __instance.m_supportedItems[0].name : "", 0, 1);
                return false;
            }
        }
        
        [HarmonyPatch(typeof(ItemStand),nameof(ItemStand.UseItem))]
        private static class ItemStand_UseItem_Patch
        {
            [UsedImplicitly]
            private static bool Prefix(ItemStand __instance)
            {
                return __instance.m_canBeRemoved || __instance.m_guardianPower is not { } gp;
            }
        }
        
        [HarmonyPatch(typeof(ItemStand),nameof(ItemStand.GetHoverText))]
        private static class ItemStand_GetHoverTextPatch
        {
            [UsedImplicitly]
            private static void Postfix(ItemStand __instance, ref string __result)
            {
                if (__instance.m_canBeRemoved || __instance.m_guardianPower is not { } gp)
                {
                    return;
                }
                __result = "";
                if (Player.m_localPlayer.m_customData.ContainsKey($"{__instance.m_supportedItems[0].name}_killed"))
                {
                    __result = 	Localization.instance.Localize(string.Concat(new string[]
                    {
                        "<color=orange>",
                        __instance.m_guardianPower.m_name,
                        "</color>\n",
                        __instance.m_guardianPower.GetTooltipString(),
                        "\n\n[<color=yellow><b>$KEY_Use</b></color>] $guardianstone_hook_activate"
                    }));
                }
            }
        }
        
        [HarmonyPatch(typeof(ItemStand),nameof(ItemStand.HaveAttachment))]
        private static class ItemStand_HaveAttachment_Patch
        {
            [UsedImplicitly]
            private static void Postfix(ItemStand __instance, ref bool __result)
            {
                if (__instance.m_canBeRemoved || __instance.m_guardianPower is not { } gp) return;
                __result = Player.m_localPlayer && Player.m_localPlayer.m_customData.ContainsKey($"{__instance.m_supportedItems[0].name}_killed");
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Character_OnDeath_Patch
        {
            [UsedImplicitly]
            private static void Prefix(Character __instance)
            {
                if (!Player.m_localPlayer || Player.m_localPlayer.IsDead() || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
                
                if (__instance.IsBoss() && __instance.GetComponent<CharacterDrop>() is { } drop)
                {
                    var findTrophy = drop.m_drops.Find(d => ObjectDB.instance.m_itemByHash.TryGetValue(d.m_prefab.name.GetStableHashCode(), out var item)
                                                            && item.GetComponent<ItemDrop>().m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy);
                    if (findTrophy is { } trophy)
                    {
                        foreach (Player sPlayer in Player.s_players.Where(x => x  && Vector3.Distance(x.transform.position, __instance.transform.position) <= BossKillDistance.Value))
                        {
                            sPlayer.m_nview.InvokeRPC("PersonalBossPowers", $"{trophy.m_prefab.name}_killed");
                        }
                    }
                }
            }
        }
        
        
        
        [HarmonyPatch(typeof(Player),nameof(Player.Awake))]
        private static class Player_Awake_Patch
        {
            [UsedImplicitly]
            private static void Postfix(Player __instance)
            {
                if (__instance.m_nview && __instance.m_nview.IsValid())
                {
                    __instance.m_nview.Register("PersonalBossPowers", (long _, string key) =>
                    {
                        __instance.m_customData[key] = "+";
                    });
                }
            }
        }
        
   
        
        
    }
}