﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pathoschild.Stardew.Automate.Framework;
using Pathoschild.Stardew.Common;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pathoschild.Stardew.Automate
{
    /// <summary>The mod entry point.</summary>
    public class AutomateMod : Mod
    {
        /*********
        ** Properties
        *********/
        /// <summary>The mod configuration.</summary>
        private ModConfig Config;

        /// <summary>Constructs machine instances.</summary>
        private readonly MachineFactory Factory = new MachineFactory();

        /// <summary>The machines to process.</summary>
        private readonly IDictionary<GameLocation, MachineMetadata[]> Machines = new Dictionary<GameLocation, MachineMetadata[]>();

        /// <summary>Whether machines are initialised.</summary>
        private bool IsReady => Game1.hasLoadedGame && this.Machines.Any();

        /****
        ** Version check
        ****/
        /// <summary>The current semantic version.</summary>
        private ISemanticVersion CurrentVersion;

        /// <summary>The newer release to notify the user about.</summary>
        private ISemanticVersion NewRelease;

        /// <summary>Whether the update-available message has been shown since the game started.</summary>
        private bool HasSeenUpdateWarning;


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides methods for interacting with the mod directory, such as read/writing a config file or custom JSON files.</param>
        public override void Entry(IModHelper helper)
        {
            // read config
            this.Config = helper.ReadConfig<ModConfig>();
            this.CurrentVersion = this.ModManifest.Version;

            // hooks for update check
            GameEvents.GameLoaded += this.GameEvents_GameLoaded;
            SaveEvents.AfterLoad += this.SaveEvents_AfterLoad;

            // hooks for automation
            LocationEvents.LocationsChanged += this.LocationEvents_LocationsChanged;
            LocationEvents.LocationObjectsChanged += this.LocationEvents_LocationObjectsChanged;
            TimeEvents.TimeOfDayChanged += this.TimeEvents_TimeOfDayChanged;
        }


        /*********
        ** Private methods
        *********/
        /****
        ** Event handlers
        ****/
        /// <summary>The method invoked when a location is added or removed.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void LocationEvents_LocationsChanged(object sender, EventArgsGameLocationsChanged e)
        {
            this.Machines.Clear();
            foreach (GameLocation location in this.Factory.GetLocationsWithChests())
                this.Machines[location] = this.Factory.GetMachinesIn(location, this.Helper.Reflection).ToArray();
        }

        /// <summary>The method invoked when an object is added or removed to a location.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void LocationEvents_LocationObjectsChanged(object sender, EventArgsLocationObjectsChanged e)
        {
            this.Machines[Game1.currentLocation] = this.Factory.GetMachinesIn(Game1.currentLocation, this.Helper.Reflection).ToArray();
        }

        /// <summary>The method invoked when the in-game clock time changes.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void TimeEvents_TimeOfDayChanged(object sender, EventArgsIntChanged e)
        {
            if (!this.IsReady)
                return;

            foreach (MachineMetadata[] machines in this.Machines.Values)
                this.ProcessMachines(machines);
        }

        /// <summary>The method invoked when the player loads the game.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void GameEvents_GameLoaded(object sender, EventArgs e)
        {
            // check for mod update
            if (this.Config.CheckForUpdates)
            {
                try
                {
                    Task.Factory.StartNew(() =>
                    {
                        Task.Factory.StartNew(() =>
                        {
                            ISemanticVersion latest = UpdateHelper.LogVersionCheck(this.Monitor, this.ModManifest.Version, "Automate").Result;
                            if (latest.IsNewerThan(this.CurrentVersion))
                                this.NewRelease = latest;
                        });
                    });
                }
                catch (Exception ex)
                {
                    this.HandleError(ex, "checking for a new version");
                }
            }
        }

        /// <summary>The method invoked when the interface has finished rendering.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void SaveEvents_AfterLoad(object sender, EventArgs e)
        {
            // render update warning
            if (this.Config.CheckForUpdates && !this.HasSeenUpdateWarning && this.NewRelease != null)
            {
                try
                {
                    this.HasSeenUpdateWarning = true;
                    CommonHelper.ShowInfoMessage($"You can update Automate from {this.CurrentVersion} to {this.NewRelease}.");
                }
                catch (Exception ex)
                {
                    this.HandleError(ex, "showing the new version available");
                }
            }

        }

        /****
        ** Methods
        ****/
        /// <summary>Process a set of machines.</summary>
        /// <param name="machines">The machines to process.</param>
        private void ProcessMachines(MachineMetadata[] machines)
        {
            foreach (MachineMetadata metadata in machines)
            {
                IMachine machine = metadata.Machine;

                switch (machine.GetState())
                {
                    case MachineState.Empty:
                        machine.Pull(metadata.Connected);
                        break;

                    case MachineState.Done:
                        if (metadata.Connected.TryPush(machine.GetOutput()))
                            machine.Reset(true);
                        break;
                }
            }
        }

        /// <summary>Log an error and warn the user.</summary>
        /// <param name="ex">The exception to handle.</param>
        /// <param name="verb">The verb describing where the error occurred (e.g. "looking that up").</param>
        private void HandleError(Exception ex, string verb)
        {
            this.Monitor.Log($"Something went wrong {verb}:\n{ex}", LogLevel.Error);
            CommonHelper.ShowErrorMessage($"Huh. Something went wrong {verb}. The error log has the technical details.");
        }
    }
}
