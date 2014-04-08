namespace PluginLiquidHarvester
{
    using Styx;
    using Styx.Common;
    using Styx.Common.Helpers;
	using Styx.CommonBot;
    using Styx.CommonBot.POI;
    using Styx.CommonBot.Frames;
    using Styx.CommonBot.Inventory;
    using Styx.CommonBot.Profiles;
    using Styx.Helpers;
    using Styx.Pathing;
    using Styx.Plugins;
    using Styx.WoWInternals;
	using Styx.WoWInternals.Misc;
	using Styx.WoWInternals.World;
    using Styx.WoWInternals.WoWObjects;

    using System;
    using System.Collections.Generic;
	using System.ComponentModel;
	using System.Data;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Linq;
	using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;
    using System.Windows.Media;
    using System.Xml.Linq;
	
    public class LiquidHarvester : HBPlugin
    {
        public override string Name { get { return "LiquidHarvester"; } }
        public override string Author { get { return "LiquidAtoR"; } }
        public override Version Version { get { return new Version(1,0,0,1); } }

        private bool _init;
        private List<ulong> L_lIgnoreUntilHarvest = new List<ulong>();

        private TimeSpan L_timeLastLoot = TimeSpan.FromSeconds(0);

        public override void Initialize()
        {
            if (_init) return;
            base.OnEnable();
            Logging.Write(LogLevel.Normal, Colors.DarkRed, "LiquidHarvester 1.0 ready for use...");
            _init = true;
        }
		
        public override void Pulse()
        {
            if (!_init)
            {
				Lua.DoString("SetCVar('AutoLootDefault','1')");
                Lua.Events.AttachEvent("LOOT_CLOSED", MobLooted);
                Mount.OnMountUp += new EventHandler<MountUpEventArgs>(MountHandler);

                _init = true;
            }

            if (StyxWoW.Me.IsActuallyInCombat)
                return;

            if (!LootTargeting.LootMobs)
                return;

            if (StyxWoW.Me.NormalBagsFull || StyxWoW.Me.FreeNormalBagSlots <= 2)
                return;

            // Don't interrupt important bot activities
            if (BotPoi.Current.Type == PoiType.Corpse ||
                BotPoi.Current.Type == PoiType.Sell)
                return;

            if (BotPoi.Current != null && BotPoi.Current.Type == PoiType.Harvest)
            {
                WoWUnit pUnit = ObjectManager.GetObjectByGuid<WoWUnit>(BotPoi.Current.Guid);

                if (pUnit != null)
                {
                    if (pUnit.Distance < pUnit.InteractRange && pUnit.InLineOfSight)
                    {
                        Blacklist.Add(pUnit.Guid, TimeSpan.FromMinutes(10));

                        if (!StyxWoW.Me.IsActuallyInCombat)
                            SleepSafely();

                        if (!StyxWoW.Me.IsActuallyInCombat)
                            pUnit.Interact();

                        SleepSafely();

                        Thread.Sleep(100);

                        BotPoi.Clear();
                    }
                }
            }

            CheckForCorpses();
        }

        private bool GatherHerbs(WoWUnit pUnit)
        {
            if (!LootTargeting.HarvestHerbs)
                return false;

            if (pUnit.SkinType != WoWCreatureSkinType.Herb)
                return false;

            return true;
        }

        private bool GatherMinerals(WoWUnit pUnit)
        {
            if (!LootTargeting.HarvestMinerals)
                return false;

            if (pUnit.SkinType != WoWCreatureSkinType.Rock)
                return false;

            return true;
        }

        private bool GatherEngineering(WoWUnit pUnit)
        {
            if (pUnit.SkinType != WoWCreatureSkinType.Bolts)
                return false;

            return true;
        }

        private bool CheckHarvest(WoWUnit pUnit)
        {
            if (!pUnit.Skinnable)
                return false;

            if (!GatherHerbs(pUnit) && !GatherMinerals(pUnit) && !GatherEngineering(pUnit))
                return false;

            if (BotPoi.Current != null &&
                (BotPoi.Current.Type == PoiType.Harvest ||
                BotPoi.Current.Type == PoiType.Loot))
            {
                if (BotPoi.Current.Location.Distance(StyxWoW.Me.Location) < pUnit.Distance)
                    return false;

                L_lIgnoreUntilHarvest.Add(BotPoi.Current.Guid);
            }

            BotPoi.Clear();
            Navigator.Clear();
            BotPoi.Current = new BotPoi(pUnit, PoiType.Harvest);

            return true;
        }

        private bool ValidLootTarget(WoWUnit pUnit)
        {
            if (Blacklist.Contains(pUnit.Guid))
                return false;

            if (L_lIgnoreUntilHarvest.Contains(pUnit.Guid))
                return false;

            if (!pUnit.IsDead)
                return false;

            if (!pUnit.KilledByMe && !pUnit.TaggedByMe)
                return false;

            if (pUnit.Distance > LootTargeting.LootRadius)
                return false;

            return true;
        }

        private void MountHandler(object sender, MountUpEventArgs e)
        {
            if (BotPoi.Current != null && 
               (BotPoi.Current.Type == PoiType.Loot ||
                BotPoi.Current.Type == PoiType.Skin ||
                BotPoi.Current.Type == PoiType.Corpse ||
                BotPoi.Current.Type == PoiType.Sell))
                return;

            if (!LootTargeting.LootMobs)
                return;

            if (StyxWoW.Me.NormalBagsFull || StyxWoW.Me.FreeNormalBagSlots <= 2)
                return;

            if (L_timeLastLoot.CompareTo(TimeSpan.FromSeconds(0)) > 0)
                SleepSafely();

            if (CheckForCorpses() && BotPoi.Current != null && BotPoi.Current.Location.Distance(StyxWoW.Me.Location) < Styx.CommonBot.Mount.MountDistance)
                e.Cancel = true;
        }

        private bool CheckForCorpses()
        {
            ObjectManager.Update();

            List<WoWUnit> lUnits = ObjectManager.GetObjectsOfType<WoWUnit>();
            bool bSuccess = false;

            foreach (WoWUnit pUnit in lUnits)
            {
                if (!ValidLootTarget(pUnit))
                    continue;

                if (BotPoi.Current != null && BotPoi.Current.Guid == pUnit.Guid)
                    continue;

                if (pUnit.Lootable)
                    continue;

                if (CheckHarvest(pUnit))
                    bSuccess = true;
            }

            if (bSuccess == false)
                bSuccess = (BotPoi.Current != null && (BotPoi.Current.Type == PoiType.Loot || BotPoi.Current.Type == PoiType.Harvest || BotPoi.Current.Type == PoiType.Skin));

            return bSuccess;
        }

        private void MobLooted(object sender, LuaEventArgs args)
        {
            L_lIgnoreUntilHarvest.Clear();
            L_timeLastLoot = TimeSpan.FromSeconds(2);

            if ((BotPoi.Current != null &&
               (BotPoi.Current.Type == PoiType.Loot ||
                BotPoi.Current.Type == PoiType.Harvest ||
                BotPoi.Current.Type == PoiType.Skin ||
                BotPoi.Current.Type == PoiType.Corpse ||
                BotPoi.Current.Type == PoiType.Sell)) ||
                StyxWoW.Me.IsActuallyInCombat)
                return;

            if (StyxWoW.Me.NormalBagsFull || StyxWoW.Me.FreeNormalBagSlots <= 2)
                return;

            if (StyxWoW.Me.IsActuallyInCombat)
                return;

            if (!LootTargeting.LootMobs)
                return;

            bool bSuccess = CheckForCorpses();

            if (!bSuccess)
            {
                WoWMovement.MoveStop();
                SleepSafely();
                bSuccess = CheckForCorpses();
            }
        }

        private void SleepSafely()
        {
            StyxWoW.SleepForLagDuration();
            Thread.Sleep(100);
        }
    }
}