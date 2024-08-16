using StardewModdingAPI;
using Microsoft.Xna.Framework;
using StardewValley;
using SObjects = StardewValley.Objects;
using SObject = StardewValley.Object;
using StardewValley.Inventories;
using System.Reflection.PortableExecutable;

namespace HopperExtractor.Patches
{
    internal class ObjectPatches
    {
        private static IMonitor Monitor;

        // call this method from your Entry class
        internal static void Initialize(IMonitor monitor)
        {
            Monitor = monitor;
        }

        /*********
        ** Private methods
        *********/
        /// <summary>The method to call after <see cref="Object.minutesElapsed"/>.</summary>
        internal static bool After_MinutesElapsed(object __instance)
        {
            if (Context.IsMainPlayer == false)
            {
                // Only main player should execute hopper logic
                return true;
            }

            if (!IsHopper(__instance))
            {
                return true;
            }
            var hopper = __instance as SObjects.Chest;
            GameLocation environment = hopper.Location;
            // Attempt extract item from object above
            if (environment.objects.TryGetValue(hopper.TileLocation - new Vector2(0, 1), out SObject objAbove))
            {
                if (objAbove.readyForHarvest.Value == true && objAbove.heldObject.Value != null)
                //&& (objAbove.Name is not "Worm Bin" or "Deluxe Worm Bin")
                {
                    TransferItem(objAbove, hopper);
                }
            }

            // Attempt autoload object below hopper
            if (environment.objects.TryGetValue(hopper.TileLocation + new Vector2(0, 1), out SObject objBelow))
            {
                var owner = GetOwner(objBelow);
                if (owner == null)
                {
                    return true;
                }

                /**
                 * Fake farmer used for muting sound in actions for actual player.
                 * Current location must be set because Cask can be loaded only in specific locations.
                 */
                var fakeFarmer = new Farmer();
                fakeFarmer.currentLocation = environment;
                AttemptAutoLoad(fakeFarmer, objBelow, hopper);
            }

            return true;
        }

        /// <summary>Get the hopper instance if the object is a hopper.</summary>
        /// <param name="obj">The object to check.</param>
        /// <param name="hopper">The hopper instance.</param>
        /// <returns>Returns whether the object is a hopper.</returns>
        private static bool IsHopper(object obj)
        {
            return obj is SObjects.Chest { SpecialChestType: SObjects.Chest.SpecialChestTypes.AutoLoader };
        }

        private static Farmer GetOwner(SObject obj)
        {
            long ownerId = obj.owner.Value;
            ////Monitor.Log($"Owner ID {obj.owner.Value}.", LogLevel.Debug);
            if (ownerId == 0)
            {
                return null;
            }

            return Game1.getFarmerMaybeOffline(ownerId);
        }

        private static void TransferItem(SObject machine, SObjects.Chest hopper)
        {
            var heldObject = machine.heldObject;
            //Monitor.Log($"Extracting {machine.DisplayName}.", LogLevel.Debug);
            if (machine is SObjects.Cask cask)
            {
                cask.agingRate.Value = 0;
                cask.daysToMature.Value = 0;
            }
            else if (machine.name is "Crystalarium")
            {
                hopper.addItem(heldObject.Value);
                Farmer fake = new Farmer();
                machine.readyForHarvest.Value = false;
                fake.currentLocation = hopper.Location;
                var original = machine.lastInputItem.First<Item>();
                machine.heldObject.Value = null;
                machine.PlaceInMachine(machine.GetMachineData(), original, false, fake, false, false);
                return;
            }
            else if (machine.name is "Worm Bin" or "Deluxe Worm Bin")
            {
                var worm = new SObject(heldObject.Value.ItemId, heldObject.Value.Stack);
                hopper.addItem(worm);
                machine.readyForHarvest.Value = false;
                machine.MinutesUntilReady = Utility.CalculateMinutesUntilMorning(Game1.timeOfDay);
                Random random = new Random();
                heldObject.Value.Stack = random.Next(4, 6);
                return;
            }
            hopper.addItem(heldObject.Value);
            machine.readyForHarvest.Value = false;
            machine.MinutesUntilReady = 0;
            machine.heldObject.Value = null;
        }
        private static void AttemptAutoLoad(Farmer who, SObject machine, SObjects.Chest hopper)
        {
            if (hopper is not SObjects.Chest { SpecialChestType: SObjects.Chest.SpecialChestTypes.AutoLoader })
            {
                //Monitor.Log($"Chest {hopper.DisplayName} is not autoloader.", LogLevel.Debug);
                return;
            }
            hopper.GetMutex().RequestLock((System.Action)(() =>
            {
                if (machine.heldObject.Value != null)
                {
                    //Monitor.Log($"Machine {machine.DisplayName} is not empty.", LogLevel.Debug);
                    hopper.GetMutex().ReleaseLock();
                    return;
                }
                machine.MinutesUntilReady = 0;

                foreach (Item obj in hopper.Items)
                {
                    if (obj.Name is "Coal") continue;
                    SObject.autoLoadFrom = hopper.Items;
                    int num = machine.performObjectDropInAction(obj, true, who) ? 1 : 0;
                    machine.heldObject.Value = null;
                    if (num != 0)
                    {
                        //Monitor.Log($"Autoloading {obj.DisplayName} to {machine.DisplayName}.", LogLevel.Debug);
                        machine.performObjectDropInAction(obj, false, who);
                        SObject.autoLoadFrom = null;
                        RemoveCoal(machine, hopper);
                        hopper.GetMutex().ReleaseLock();
                        return;
                    }
                }
                SObject.autoLoadFrom = null;
                hopper.GetMutex().ReleaseLock();
            }));
        }
        private static void RemoveCoal(SObject machine, SObjects.Chest hopper)
        {
            if (machine.Name is "Geode Crusher")
            {
                foreach (Item obj in hopper.Items)
                    if (obj.Name is "Coal")
                    {
                        obj.Stack--;
                        break;
                    }
            }
        }
    }
}