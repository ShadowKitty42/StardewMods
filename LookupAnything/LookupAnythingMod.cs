﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathoschild.Stardew.Common;
using Pathoschild.Stardew.LookupAnything.Components;
using Pathoschild.Stardew.LookupAnything.Framework;
using Pathoschild.Stardew.LookupAnything.Framework.Constants;
using Pathoschild.Stardew.LookupAnything.Framework.Subjects;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pathoschild.Stardew.LookupAnything
{
    /// <summary>The mod entry point.</summary>
    public class LookupAnythingMod : Mod
    {
        /*********
        ** Properties
        *********/
        /****
        ** Configuration
        ****/
        /// <summary>The mod configuration.</summary>
        private ModConfig Config;

        /// <summary>Provides metadata that's not available from the game data directly.</summary>
        private Metadata Metadata;

        /// <summary>The name of the file containing data for the <see cref="Metadata"/> field.</summary>
        private readonly string DatabaseFileName = "data.json";

#if TEST_BUILD
        /// <summary>Reloads the <see cref="Metadata"/> when the underlying file changes.</summary>
        private FileSystemWatcher OverrideFileWatcher;
#endif

        /****
        ** Version check
        ****/
        /// <summary>The current semantic version.</summary>
        private ISemanticVersion CurrentVersion;

        /// <summary>The newer release to notify the user about.</summary>
        private ISemanticVersion NewRelease;

        /// <summary>Whether the update-available message has been shown since the game started.</summary>
        private bool HasSeenUpdateWarning;

        /****
        ** Validation
        ****/
        /// <summary>Whether the metadata validation passed.</summary>
        private bool IsDataValid;

        /****
        ** State
        ****/
        /// <summary>The previous menus shown before the current lookup UI was opened.</summary>
        private readonly Stack<IClickableMenu> PreviousMenus = new Stack<IClickableMenu>();

        /// <summary>Finds and analyses lookup targets in the world.</summary>
        private TargetFactory TargetFactory;

        /// <summary>Draws debug information to the screen.</summary>
        private DebugInterface DebugInterface;


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides methods for interacting with the mod directory, such as read/writing a config file or custom JSON files.</param>
        public override void Entry(IModHelper helper)
        {
            // load config
            this.Config = this.Helper.ReadConfig<RawModConfig>().GetParsed(this.Monitor);

            // load database
            this.LoadMetadata();
#if TEST_BUILD
                this.OverrideFileWatcher = new FileSystemWatcher(this.PathOnDisk, this.DatabaseFileName)
                {
                    EnableRaisingEvents = true
                };
                this.OverrideFileWatcher.Changed += (sender, e) =>
                {
                    this.LoadMetadata();
                    this.TargetFactory = new TargetFactory(this.Metadata);
                    this.DebugInterface = new DebugInterface(this.TargetFactory, this.Config)
                    {
                        Enabled = this.DebugInterface.Enabled
                    };
                };
#endif

            // initialise functionality
            this.CurrentVersion = new SemanticVersion(this.ModManifest.Version.ToString());
            this.TargetFactory = new TargetFactory(this.Metadata, this.Helper.Reflection);
            this.DebugInterface = new DebugInterface(this.TargetFactory, this.Config, this.Monitor);

            // hook up events
            {
                // reset low-level cache once per game day (used for expensive queries that don't change within a day)
                SaveEvents.AfterLoad += (sender, e) => GameHelper.ResetCache(this.Metadata, this.Helper.Reflection);
                SaveEvents.AfterSave += (sender, e) => GameHelper.ResetCache(this.Metadata, this.Helper.Reflection);

                // hook up game events
                GameEvents.GameLoaded += (sender, e) => this.ReceiveGameLoaded();
                GraphicsEvents.OnPostRenderHudEvent += (sender, e) => this.ReceiveInterfaceRendering(Game1.spriteBatch);
                MenuEvents.MenuClosed += (sender, e) => this.ReceiveMenuClosed(e.PriorMenu);

                // hook up keyboard
                if (this.Config.Keyboard.HasAny())
                {
                    ControlEvents.KeyPressed += (sender, e) => this.ReceiveKeyPress(e.KeyPressed, this.Config.Keyboard);
                    if (this.Config.HideOnKeyUp)
                        ControlEvents.KeyReleased += (sender, e) => this.ReceiveKeyRelease(e.KeyPressed, this.Config.Keyboard);
                }

                // hook up controller
                if (this.Config.Controller.HasAny())
                {
                    ControlEvents.ControllerButtonPressed += (sender, e) => this.ReceiveKeyPress(e.ButtonPressed, this.Config.Controller);
                    ControlEvents.ControllerTriggerPressed += (sender, e) => this.ReceiveKeyPress(e.ButtonPressed, this.Config.Controller);
                    if (this.Config.HideOnKeyUp)
                    {
                        ControlEvents.ControllerButtonReleased += (sender, e) => this.ReceiveKeyRelease(e.ButtonReleased, this.Config.Controller);
                        ControlEvents.ControllerTriggerReleased += (sender, e) => this.ReceiveKeyRelease(e.ButtonReleased, this.Config.Controller);
                    }
                }
            }

            // validate metadata
            this.IsDataValid = this.Metadata.LooksValid();
            if (!this.IsDataValid)
            {
                this.Monitor.Log("The data.json file seems to be missing or corrupt. Lookups will be disabled.", LogLevel.Error);
                this.IsDataValid = false;
            }
        }

        /*********
        ** Private methods
        *********/
        /****
        ** Event handlers
        ****/
        /// <summary>The method invoked when the player loads the game.</summary>
        private void ReceiveGameLoaded()
        {
            // check for an updated version
            if (this.Config.CheckForUpdates)
            {
                Task.Factory.StartNew(() =>
                {
                    ISemanticVersion latest = UpdateHelper.LogVersionCheck(this.Monitor, this.ModManifest.Version, "LookupAnything").Result;
                    if (latest.IsNewerThan(this.CurrentVersion))
                        this.NewRelease = latest;
                });
            }
        }

        /// <summary>The method invoked when the player presses an input button.</summary>
        /// <typeparam name="TKey">The input type.</typeparam>
        /// <param name="key">The pressed input.</param>
        /// <param name="map">The configured input mapping.</param>
        private void ReceiveKeyPress<TKey>(TKey key, InputMapConfiguration<TKey> map)
        {
            if (!map.IsValidKey(key))
                return;

            // perform bound action
            this.Monitor.InterceptErrors("handling your input", $"handling input '{key}'", () =>
            {
                if (key.Equals(map.ToggleLookup))
                    this.ToggleLookup(LookupMode.Cursor);
                else if (key.Equals(map.ToggleLookupInFrontOfPlayer))
                    this.ToggleLookup(LookupMode.FacingPlayer);
                else if (key.Equals(map.ScrollUp))
                    (Game1.activeClickableMenu as LookupMenu)?.ScrollUp();
                else if (key.Equals(map.ScrollDown))
                    (Game1.activeClickableMenu as LookupMenu)?.ScrollDown();
                else if (key.Equals(map.ToggleDebug))
                    this.DebugInterface.Enabled = !this.DebugInterface.Enabled;
            });
        }

        /// <summary>The method invoked when the player presses an input button.</summary>
        /// <typeparam name="TKey">The input type.</typeparam>
        /// <param name="key">The pressed input.</param>
        /// <param name="map">The configured input mapping.</param>
        private void ReceiveKeyRelease<TKey>(TKey key, InputMapConfiguration<TKey> map)
        {
            if (!map.IsValidKey(key))
                return;

            // perform bound action
            this.Monitor.InterceptErrors("handling your input", $"handling input '{key}'", () =>
            {
                if (key.Equals(map.ToggleLookup) || key.Equals(map.ToggleLookupInFrontOfPlayer))
                {
                    this.PreviousMenus.Clear();
                    this.HideLookup();
                }
            });
        }

        /// <summary>The method invoked when the player closes a displayed menu.</summary>
        /// <param name="closedMenu">The menu which the player just closed.</param>
        private void ReceiveMenuClosed(IClickableMenu closedMenu)
        {
            // restore the previous menu if it was hidden to show the lookup UI
            this.Monitor.InterceptErrors("restoring the previous menu", () =>
            {
                if (closedMenu is LookupMenu && this.PreviousMenus.Any())
                    Game1.activeClickableMenu = this.PreviousMenus.Pop();
            });
        }

        /// <summary>The method invoked when the interface is rendering.</summary>
        /// <param name="spriteBatch">The sprite batch being drawn.</param>
        private void ReceiveInterfaceRendering(SpriteBatch spriteBatch)
        {
            // render debug interface
            if (this.DebugInterface.Enabled)
                this.DebugInterface.Draw(spriteBatch);

            // render update warning
            if (this.Config.CheckForUpdates && !this.HasSeenUpdateWarning && this.NewRelease != null)
            {
                this.HasSeenUpdateWarning = true;
                GameHelper.ShowInfoMessage($"You can update Lookup Anything from {this.CurrentVersion} to {this.NewRelease}.");
            }
        }

        /****
        ** Helpers
        ****/
        /// <summary>Show the lookup UI for the current target.</summary>
        /// <param name="lookupMode">The lookup target mode.</param>
        private void ToggleLookup(LookupMode lookupMode)
        {
            if (Game1.activeClickableMenu is LookupMenu)
                this.HideLookup();
            else
                this.ShowLookup(lookupMode);
        }

        /// <summary>Show the lookup UI for the current target.</summary>
        /// <param name="lookupMode">The lookup target mode.</param>
        private void ShowLookup(LookupMode lookupMode)
        {
            // disable lookups if metadata is invalid
            if (!this.IsDataValid)
            {
                GameHelper.ShowErrorMessage("The mod doesn't seem to be installed correctly: its data.json file is missing or corrupt.");
                return;
            }

            // show menu
            StringBuilder logMessage = new StringBuilder("Received a lookup request...");
            this.Monitor.InterceptErrors("looking that up", () =>
            {
                try
                {
                    // get target
                    ISubject subject = this.GetSubject(logMessage, lookupMode);
                    if (subject == null)
                    {
                        this.Monitor.Log($"{logMessage} no target found.", LogLevel.Trace);
                        return;
                    }

                    // show lookup UI
                    this.Monitor.Log(logMessage.ToString(), LogLevel.Trace);
                    this.ShowLookupFor(subject);
                }
                catch
                {
                    this.Monitor.Log($"{logMessage} an error occurred.", LogLevel.Trace);
                    throw;
                }
            });
        }

        /// <summary>Show a lookup menu for the given subject.</summary>
        /// <param name="subject">The subject to look up.</param>
        internal void ShowLookupFor(ISubject subject)
        {
            this.Monitor.InterceptErrors("looking that up", () =>
            {
                this.Monitor.Log($"Showing {subject.GetType().Name}::{subject.Type}::{subject.Name}.", LogLevel.Trace);
                if (Game1.activeClickableMenu != null)
                    this.PreviousMenus.Push(Game1.activeClickableMenu);
                Game1.activeClickableMenu = new LookupMenu(subject, this.Metadata, this.Monitor, this.Helper.Reflection, this.Config.ScrollAmount, this.Config.ShowDataMiningFields, this.ShowLookupFor);
            });
        }

        /// <summary>Get the most relevant subject under the player's cursor.</summary>
        /// <param name="logMessage">The log message to which to append search details.</param>
        /// <param name="lookupMode">The lookup target mode.</param>
        private ISubject GetSubject(StringBuilder logMessage, LookupMode lookupMode)
        {
            // menu under cursor
            if (lookupMode == LookupMode.Cursor)
            {
                Vector2 cursorPos = GameHelper.GetScreenCoordinatesFromCursor();

                // try menu
                if (Game1.activeClickableMenu != null)
                {
                    logMessage.Append($" searching the open '{Game1.activeClickableMenu.GetType().Name}' menu...");
                    return this.TargetFactory.GetSubjectFrom(Game1.activeClickableMenu, cursorPos);
                }

                // try HUD under cursor
                foreach (IClickableMenu menu in Game1.onScreenMenus)
                {
                    if (menu.isWithinBounds((int)cursorPos.X, (int)cursorPos.Y))
                    {
                        logMessage.Append($" searching the on-screen '{menu.GetType().Name}' menu...");
                        return this.TargetFactory.GetSubjectFrom(menu, cursorPos);
                    }
                }
            }

            // try world
            if (Game1.activeClickableMenu == null)
            {
                logMessage.Append(" searching the world...");
                return this.TargetFactory.GetSubjectFrom(Game1.player, Game1.currentLocation, lookupMode, this.Config.EnableTileLookups);
            }

            // not found
            return null;
        }

        /// <summary>Show the lookup UI for the current target.</summary>
        private void HideLookup()
        {
            this.Monitor.InterceptErrors("closing the menu", () =>
            {
                if (Game1.activeClickableMenu is LookupMenu)
                {
                    Game1.playSound("bigDeSelect"); // match default behaviour when closing a menu
                    Game1.activeClickableMenu = null;
                }
            });
        }

        /// <summary>Load the file containing metadata that's not available from the game directly.</summary>
        private void LoadMetadata()
        {
            this.Monitor.InterceptErrors("loading metadata", () =>
            {
                this.Metadata = this.Helper.ReadJsonFile<Metadata>(this.DatabaseFileName);
            });
        }
    }
}
