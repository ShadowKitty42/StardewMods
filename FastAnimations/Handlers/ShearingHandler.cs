﻿using Microsoft.Xna.Framework;
using Pathoschild.Stardew.FastAnimations.Framework;
using StardewValley;

namespace Pathoschild.Stardew.FastAnimations.Handlers
{
    /// <summary>Handles the wool shearing animation.</summary>
    /// <remarks>See game logic in <see cref="StardewValley.Tools.Shears.beginUsing"/>.</remarks>
    internal class ShearingHandler : IAnimationHandler
    {
        /*********
        ** Properties
        *********/
        /// <summary>The animation speed multiplier to apply.</summary>
        private readonly int Multiplier;

        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="multiplier">The animation speed multiplier to apply.</param>
        public ShearingHandler(int multiplier)
        {
            this.Multiplier = multiplier;
        }
        /// <summary>Get whether the animation is currently active.</summary>
        /// <param name="playerAnimationID">The player's current animation ID.</param>
        public bool IsEnabled(int playerAnimationID)
        {
            return
                Game1.player.Sprite.CurrentAnimation != null
                && (
                    playerAnimationID == FarmerSprite.shearDown
                    || playerAnimationID == FarmerSprite.shearLeft
                    || playerAnimationID == FarmerSprite.shearRight
                    || playerAnimationID == FarmerSprite.shearUp
                );
        }

        /// <summary>Perform any logic needed on update while the animation is active.</summary>
        /// <param name="playerAnimationID">The player's current animation ID.</param>
        public void Update(int playerAnimationID)
        {
            // speed up animation
            GameTime gameTime = Game1.currentGameTime;
            GameLocation location = Game1.player.currentLocation;
            for (int i = 1; i < this.Multiplier; i++)
                Game1.player.Update(gameTime, location);
        }
    }
}
