﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Graphics.Effects;
using Windows.UI;
using Windows.UI.Composition;
using JetBrains.Annotations;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using UICompositionAnimations.Enums;
using UICompositionAnimations.Helpers;

namespace UICompositionAnimations.Behaviours
{
    /// <summary>
    /// An helper class that manages the tint and noise layers on the custom acrylic brush effect
    /// </summary>
    internal static class AcrylicEffectHelper
    {
        /// <summary>
        /// Concatenates the source effect with a tint overlay and a border effect
        /// </summary>
        /// <param name="compositor">The current <see cref="Compositor"/> object to use</param>
        /// <param name="source">The source effect to insert in the pipeline</param>
        /// <param name="parameters">A dictionary to use to keep track of reference parameters to add when creating a <see cref="CompositionEffectFactory"/></param>
        /// <param name="color">The tint color</param>
        /// <param name="colorMix">The amount of tint color to apply</param>
        /// <param name="canvas">The optional <see cref="CanvasControl"/> to use to generate the image for the <see cref="BorderEffect"/></param>
        /// <param name="uri">The path to the source image to use for the <see cref="BorderEffect"/></param>
        /// <param name="options">Indicates whether or not to force the reload of the Win2D image</param>
        /// <returns>The resulting effect through the pipeline</returns>
        /// <remarks>The method does side effect on the <paramref name="parameters"/> variable</remarks>
        [MustUseReturnValue, ItemNotNull]
        public static async Task<IGraphicsEffect> ConcatenateEffectWithTintAndBorderAsync(
            [NotNull] Compositor compositor,
            [NotNull] IGraphicsEffectSource source, [NotNull] IDictionary<String, CompositionBrush> parameters,
            Color color, float colorMix,
            [CanBeNull] CanvasControl canvas, [NotNull] Uri uri, CacheLoadingMode options)
        {
            // Setup the tint effect
            ArithmeticCompositeEffect tint = new ArithmeticCompositeEffect
            {
                MultiplyAmount = 0,
                Source1Amount = 1 - colorMix,
                Source2Amount = colorMix, // Mix the background with the desired tint color
                Source1 = source,
                Source2 = new ColorSourceEffect { Color = color }
            };

            // Get the noise brush using Win2D
            CompositionSurfaceBrush noiseBitmap = canvas == null
                ? await Win2DImageHelper.LoadImageAsync(compositor, uri, options)
                : await Win2DImageHelper.LoadImageAsync(compositor, canvas, uri, options);

            // Make sure the Win2D brush was loaded correctly
            if (noiseBitmap != null)
            {
                // Layer 4 - Noise effect
                BorderEffect borderEffect = new BorderEffect
                {
                    ExtendX = CanvasEdgeBehavior.Wrap,
                    ExtendY = CanvasEdgeBehavior.Wrap,
                    Source = new CompositionEffectSourceParameter(nameof(noiseBitmap))
                };
                BlendEffect blendEffect = new BlendEffect
                {
                    Background = tint,
                    Foreground = borderEffect,
                    Mode = BlendEffectMode.Overlay
                };
                parameters.Add(nameof(noiseBitmap), noiseBitmap);
                return blendEffect;
            }
            return tint;
        }
    }
}
