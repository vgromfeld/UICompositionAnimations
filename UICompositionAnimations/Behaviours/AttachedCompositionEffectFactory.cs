﻿using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Hosting;
using JetBrains.Annotations;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.Canvas.UI.Xaml;
using UICompositionAnimations.Behaviours.Effects;
using UICompositionAnimations.Behaviours.Effects.Base;
using UICompositionAnimations.Composition;
using UICompositionAnimations.Enums;
using UICompositionAnimations.Helpers;

namespace UICompositionAnimations.Behaviours
{
    /// <summary>
    /// A static class that manages the creation of attached composition effects
    /// </summary>

    public static class AttachedCompositionEffectsFactory
    {
        #region Static effects

        /// <summary>
        /// Creates a new <see cref="AttachedCompositionEffectWithAutoResize{T}"/> instance for the target element
        /// </summary>
        /// <typeparam name="T">The type of element to blur</typeparam>
        /// <param name="element">The target element</param>
        /// <param name="blur">The amount of blur to apply to the element</param>
        /// <param name="ms">The duration of the initial blur animation, in milliseconds</param>
        [MustUseReturnValue, NotNull]
        public static AttachedCompositionEffectWithAutoResize<T> GetAttachedBlur<T>(
            [NotNull] this T element, float blur, int ms) where T : FrameworkElement
        {
            // Get the visual and the compositor
            Visual visual = element.GetVisual();
            Compositor compositor = visual.Compositor;

            // Create the blur effect and the effect factory
            GaussianBlurEffect blurEffect = new GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = 0f,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("source")
            };
            CompositionEffectFactory effectFactory = compositor.CreateEffectFactory(blurEffect, new[] { "Blur.BlurAmount" });

            // Setup the rest of the effect
            CompositionEffectBrush effectBrush = effectFactory.CreateBrush();
            effectBrush.SetSourceParameter("source", compositor.CreateBackdropBrush());

            // Assign the effect to a brush and display it
            SpriteVisual sprite = compositor.CreateSpriteVisual();
            sprite.Brush = effectBrush;
            sprite.Size = new Vector2((float)element.ActualWidth, (float)element.ActualHeight);
            ElementCompositionPreview.SetElementChildVisual(element, sprite);

            // Animate the blur amount
            effectBrush.StartAnimationAsync("Blur.BlurAmount", blur, TimeSpan.FromMilliseconds(ms));

            // Prepare and return the manager
            return new AttachedCompositionEffectWithAutoResize<T>(element, sprite, effectBrush);
        }

        /// <summary>
        /// Creates an effect brush that's similar to the official Acrylic brush in the Fall Creator's Update.
        /// The pipeline uses the following effects: BackdropBrush > <see cref="GaussianBlurEffect"/> >
        /// <see cref="ColorSourceEffect"/> > <see cref="BorderEffect"/> with customizable blend factors for each couple of layers
        /// </summary>
        /// <typeparam name="TSource">The type of the element that will be the source for the composition effect</typeparam>
        /// <typeparam name="T">The type of the target element that will host the resulting <see cref="SpriteVisual"/></typeparam>
        /// <param name="element">The target element that will host the effect</param>
        /// <param name="target">The target host for the resulting effect</param>
        /// <param name="blur">The amount of blur to apply to the element</param>
        /// <param name="ms">The duration of the initial blur animation, in milliseconds</param>
        /// <param name="color">The tint color for the effect</param>
        /// <param name="colorMix">The opacity of the color over the blurred background</param>
        /// <param name="canvas">The source <see cref="CanvasControl"/> to generate the noise image using Win2D</param>
        /// <param name="uri">The path of the noise image to use</param>
        /// <param name="timeThreshold">The maximum time to wait for the Win2D device to be restored in case of initial failure</param>
        /// <param name="reload">Indicates whether or not to force the reload of the Win2D image</param>
        /// <param name="fadeIn">Indicates whether or not to fade the effect in</param>
        [MustUseReturnValue, NotNull]
        public static async Task<AttachedStaticCompositionEffect<T>> GetAttachedInAppSemiAcrylicEffectAsync<TSource, T>(
            [NotNull] this TSource element, T target, float blur, int ms, Color color, float colorMix,
            [NotNull] CanvasControl canvas, [NotNull] Uri uri, int timeThreshold = 1000, bool reload = false, bool fadeIn = false)
            where TSource : FrameworkElement
            where T : FrameworkElement
        {
            // Percentage check
            if (colorMix <= 0 || colorMix >= 1) throw new ArgumentOutOfRangeException("The mix factors must be in the [0,1] range");
            if (timeThreshold <= 0) throw new ArgumentOutOfRangeException("The time threshold must be a positive number");

            // Setup the compositor
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;

            // Prepare a luminosity to alpha effect to adjust the background contrast
            CompositionBackdropBrush backdropBrush = compositor.CreateBackdropBrush();
            const String
                blurName = "Blur",
                blurParameterName = "Blur.BlurAmount";
            GaussianBlurEffect blurEffect = new GaussianBlurEffect
            {
                Name = blurName,
                BlurAmount = 0f,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter(nameof(backdropBrush))
            };

            // Background with blur and tint overlay
            ArithmeticCompositeEffect composite = new ArithmeticCompositeEffect
            {
                MultiplyAmount = 0,
                Source1Amount = 1 - colorMix,
                Source2Amount = colorMix, // Mix the background with the desired tint color
                Source1 = blurEffect,
                Source2 = new ColorSourceEffect { Color = color }
            };
            IDictionary<String, CompositionBrush> sourceParameters = new Dictionary<String, CompositionBrush>
            {
                { nameof(backdropBrush), backdropBrush }
            };

            // Get the noise brush using Win2D
            CompositionSurfaceBrush noiseBitmap = await LoadWin2DSurfaceBrushFromImageAsync(compositor, canvas, uri, timeThreshold, reload);

            // Make sure the Win2D brush was loaded correctly
            CompositionEffectFactory factory;
            if (noiseBitmap != null)
            {
                // Noise effect
                BorderEffect borderEffect = new BorderEffect
                {
                    ExtendX = CanvasEdgeBehavior.Wrap,
                    ExtendY = CanvasEdgeBehavior.Wrap,
                    Source = new CompositionEffectSourceParameter(nameof(noiseBitmap))
                };
                BlendEffect blendEffect = new BlendEffect
                {
                    Background = composite,
                    Foreground = borderEffect,
                    Mode = BlendEffectMode.Overlay
                };
                factory = compositor.CreateEffectFactory(blendEffect, new[] { blurParameterName });
                sourceParameters.Add(nameof(noiseBitmap), noiseBitmap);
            }
            else
            {
                // Fallback, just use the first layers
                factory = compositor.CreateEffectFactory(composite, new[] { blurParameterName });
            }

            // Create the effect factory and apply the final effect
            CompositionEffectBrush effectBrush = factory.CreateBrush();
            foreach (KeyValuePair<String, CompositionBrush> pair in sourceParameters)
            {
                effectBrush.SetSourceParameter(pair.Key, pair.Value);
            }

            // Create the sprite to display and add it to the visual tree
            SpriteVisual sprite = compositor.CreateSpriteVisual();
            sprite.Brush = effectBrush;
            if (target.ActualHeight + target.ActualWidth > 0.1)
            {
                sprite.Size = new Vector2((float)target.ActualWidth, (float)target.ActualHeight);
            }
            else
            {
                // Schedule the size update
                void OneShotResizer(object sender, SizeChangedEventArgs e)
                {
                    sprite.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
                    target.SizeChanged -= OneShotResizer;
                }
                target.SizeChanged += OneShotResizer;
            }

            // Assign the visual
            if (fadeIn)
            {
                sprite.StopAnimation("Opacity");
                sprite.Opacity = 0;
            }
            ElementCompositionPreview.SetElementChildVisual(target, sprite);
            if (fadeIn)
            {
                // Fade the effect in
                ScalarKeyFrameAnimation opacityAnimation = sprite.Compositor.CreateScalarKeyFrameAnimation(1, 0,
                    TimeSpan.FromMilliseconds(ms), null, sprite.GetEasingFunction(EasingFunctionNames.Linear));
                sprite.StartAnimation("Opacity", opacityAnimation);
            }

            // Animate the blur and return the result
            effectBrush.StartAnimationAsync(blurParameterName, blur, TimeSpan.FromMilliseconds(ms)).Forget();
            return new AttachedStaticCompositionEffect<T>(target, sprite, effectBrush);
        }

        /// <summary>
        /// Creates a new <see cref="AttachedStaticCompositionEffect{T}"/> instance for the target element
        /// </summary>
        /// <typeparam name="T">The type of element to use to host the effect</typeparam>
        /// <param name="element">The target element</param>
        [MustUseReturnValue, NotNull]
        public static AttachedStaticCompositionEffect<T> GetAttachedHostBackdropBlur<T>(
            [NotNull] this T element) where T : FrameworkElement
        {
            // Setup the host backdrop effect
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;
            CompositionBackdropBrush brush = compositor.CreateHostBackdropBrush();
            SpriteVisual sprite = compositor.CreateSpriteVisual();
            sprite.Brush = brush;
            sprite.Size = new Vector2((float)element.ActualWidth, (float)element.ActualHeight);
            ElementCompositionPreview.SetElementChildVisual(element, sprite);
            return new AttachedStaticCompositionEffect<T>(element, sprite, brush);
        }

        /// <summary>
        /// Gets a shared semaphore to avoid loading multiple Win2D resources at the same time
        /// </summary>
        private static readonly SemaphoreSlim Win2DSemaphore = new SemaphoreSlim(1);

        /// <summary>
        /// Gets the local cache mapping for previously loaded Win2D images
        /// </summary>
        private static readonly IDictionary<String, CompositionSurfaceBrush> SurfacesCache = new Dictionary<String, CompositionSurfaceBrush>();

        /// <summary>
        /// Loads a <see cref="CompositionSurfaceBrush"/> instance with the target image
        /// </summary>
        /// <param name="compositor">The compositor to use to render the Win2D image</param>
        /// <param name="canvas">The <see cref="CanvasControl"/> to use to load the target image</param>
        /// <param name="uri">The path to the image to load</param>
        /// <param name="timeThreshold">The maximum time to wait for the Win2D device to be restored in case of initial failure/></param>
        /// <param name="reload">Indicates whether or not to force the reload of the Win2D image</param>
        [ItemCanBeNull]
        private static async Task<CompositionSurfaceBrush> LoadWin2DSurfaceBrushFromImageAsync(
            [NotNull] Compositor compositor, [NotNull] CanvasControl canvas, [NotNull] Uri uri, int timeThreshold = 1000, bool reload = false)
        {
            TaskCompletionSource<CompositionSurfaceBrush> tcs = new TaskCompletionSource<CompositionSurfaceBrush>();
            async Task<CompositionSurfaceBrush> LoadImageAsync(bool shouldThrow)
            {
                // Load the image - this will only succeed when there's an available Win2D device
                try
                {
                    using (CanvasBitmap bitmap = await CanvasBitmap.LoadAsync(canvas, uri))
                    {
                        // Get the device and the target surface
                        CompositionGraphicsDevice device = CanvasComposition.CreateCompositionGraphicsDevice(compositor, canvas.Device);
                        CompositionDrawingSurface surface = device.CreateDrawingSurface(default(Size),
                            DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);

                        // Calculate the surface size
                        Size size = bitmap.Size;
                        CanvasComposition.Resize(surface, size);

                        // Draw the image on the surface and get the resulting brush
                        using (CanvasDrawingSession session = CanvasComposition.CreateDrawingSession(surface))
                        {
                            session.Clear(Color.FromArgb(0, 0, 0, 0));
                            session.DrawImage(bitmap, new Rect(0, 0, size.Width, size.Height), new Rect(0, 0, size.Width, size.Height));
                            CompositionSurfaceBrush brush = surface.Compositor.CreateSurfaceBrush(surface);
                            return brush;
                        }
                    }
                }
                catch when (!shouldThrow)
                {
                    // Win2D error, just ignore and continue
                    return null;
                }
            }
            async void Canvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
            {
                // Cancel previous actions
                args.GetTrackedAction()?.Cancel();

                // Load the image and notify the canvas
                Task<CompositionSurfaceBrush> task = LoadImageAsync(false);
                IAsyncAction action = task.AsAsyncAction();
                try
                {
                    args.TrackAsyncAction(action);
                    CompositionSurfaceBrush brush = await task;
                    action.Cancel();
                    tcs.TrySetResult(brush);
                }
                catch (COMException)
                {
                    // Somehow another action was still being tracked
                    tcs.TrySetResult(null);
                }
            }

            // Lock the semaphore and check the cache first
            await Win2DSemaphore.WaitAsync();
            if (!reload && SurfacesCache.TryGetValue(uri.ToString(), out CompositionSurfaceBrush cached))
            {
                Win2DSemaphore.Release();
                return cached;
            }

            // Load the image
            canvas.CreateResources += Canvas_CreateResources;
            try
            {
                // This will throw and the canvas will re-initialize the Win2D device if needed
                await LoadImageAsync(true);
            }
            catch (ArgumentException)
            {
                // Just ignore here
            }
            catch
            {
                // Win2D messed up big time
                tcs.TrySetResult(null);
            }
            await Task.WhenAny(tcs.Task, Task.Delay(timeThreshold).ContinueWith(t => tcs.TrySetResult(null)));
            canvas.CreateResources -= Canvas_CreateResources;
            CompositionSurfaceBrush instance = tcs.Task.Result;
            String key = uri.ToString();
            if (instance != null && !SurfacesCache.ContainsKey(key)) SurfacesCache.Add(key, instance);
            Win2DSemaphore.Release();
            return instance;
        }

        /// <summary>
        /// Creates an effect brush that's similar to the official Acrylic brush in the Fall Creator's Update.
        /// The pipeline uses the following effects: HostBackdropBrush > <see cref="LuminanceToAlphaEffect"/> >
        /// <see cref="OpacityEffect"/> > <see cref="BlendEffect"/> > <see cref="ArithmeticCompositeEffect"/> >
        /// <see cref="ColorSourceEffect"/> > <see cref="BorderEffect"/> with customizable blend factors for each couple of layers
        /// </summary>
        /// <typeparam name="T">The type of the target element that will host the resulting <see cref="SpriteVisual"/></typeparam>
        /// <param name="element">The target element that will host the effect</param>
        /// <param name="color">The tint color for the effect</param>
        /// <param name="colorMix">The opacity of the color over the blurred background</param>
        /// <param name="canvas">The source <see cref="CanvasControl"/> to generate the noise image using Win2D</param>
        /// <param name="uri">The path of the noise image to use</param>
        /// <param name="timeThreshold">The maximum time to wait for the Win2D device to be restored in case of initial failure/></param>
        /// <param name="reload">Indicates whether or not to force the reload of the Win2D image</param>
        public static async Task<AttachedStaticCompositionEffect<T>> GetAttachedSemiAcrylicEffectAsync<T>(
            [NotNull] this T element, Color color, float colorMix,
            [NotNull] CanvasControl canvas, [NotNull] Uri uri, int timeThreshold = 1000, bool reload = false) where T : FrameworkElement
        {
            // Percentage check
            if (colorMix <= 0 || colorMix >= 1) throw new ArgumentOutOfRangeException("The mix factors must be in the [0,1] range");
            if (timeThreshold <= 0) throw new ArgumentOutOfRangeException("The time threshold must be a positive number");

            // Setup the compositor
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;

            // Prepare a luminosity to alpha effect to adjust the background contrast
            CompositionBackdropBrush hostBackdropBrush = compositor.CreateHostBackdropBrush();
            CompositionEffectSourceParameter backgroundParameter = new CompositionEffectSourceParameter(nameof(hostBackdropBrush));
            LuminanceToAlphaEffect alphaEffect = new LuminanceToAlphaEffect { Source = backgroundParameter };
            OpacityEffect opacityEffect = new OpacityEffect
            {
                Source = alphaEffect,
                Opacity = 0.4f // Reduce the amount of the effect to avoid making bright areas completely black
            };

            // Layer [0,1,3] - Desktop background with blur and tint overlay
            BlendEffect alphaBlend = new BlendEffect
            {
                Background = backgroundParameter,
                Foreground = opacityEffect,
                Mode = BlendEffectMode.Overlay
            };
            ArithmeticCompositeEffect composite = new ArithmeticCompositeEffect
            {
                MultiplyAmount = 0,
                Source1Amount = 1 - colorMix,
                Source2Amount = colorMix, // Mix the background with the desired tint color
                Source1 = alphaBlend,
                Source2 = new ColorSourceEffect { Color = color }
            };
            IDictionary<String, CompositionBrush> sourceParameters = new Dictionary<String, CompositionBrush>
            {
                { nameof(hostBackdropBrush), hostBackdropBrush }
            };

            // Get the noise brush using Win2D
            CompositionSurfaceBrush noiseBitmap = await LoadWin2DSurfaceBrushFromImageAsync(compositor, canvas, uri, timeThreshold, reload);

            // Make sure the Win2D brush was loaded correctly
            CompositionEffectFactory factory;
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
                    Background = composite,
                    Foreground = borderEffect,
                    Mode = BlendEffectMode.Overlay
                };
                factory = compositor.CreateEffectFactory(blendEffect);
                sourceParameters.Add(nameof(noiseBitmap), noiseBitmap);
            }
            else
            {
                // Fallback, just use the first layers
                factory = compositor.CreateEffectFactory(composite);
            }

            // Create the effect factory and apply the final effect
            CompositionEffectBrush effectBrush = factory.CreateBrush();
            foreach (KeyValuePair<String, CompositionBrush> pair in sourceParameters)
            {
                effectBrush.SetSourceParameter(pair.Key, pair.Value);
            }

            // Create the sprite to display and add it to the visual tree
            SpriteVisual sprite = compositor.CreateSpriteVisual();
            sprite.Brush = effectBrush;
            sprite.Size = new Vector2((float)element.ActualWidth, (float)element.ActualHeight);
            ElementCompositionPreview.SetElementChildVisual(element, sprite);
            return new AttachedStaticCompositionEffect<T>(element, sprite, effectBrush);
        }

        #endregion

        #region Animated effects

        /// <summary>
        /// Creates a new <see cref="AttachedAnimatableCompositionEffect{T}"/> instance for the target element
        /// </summary>
        /// <typeparam name="T">The type of element to blur</typeparam>
        /// <param name="element">The target element</param>
        /// <param name="on">The amount of saturation effect to apply</param>
        /// <param name="off">The default amount of saturation effect to apply</param>
        /// <param name="initiallyVisible">Indicates whether or not to apply the effect right away</param>
        [MustUseReturnValue, NotNull]
        public static async Task<AttachedAnimatableCompositionEffect<T>> GetAttachedAnimatableSaturationEffectAsync<T>(
            [NotNull] this T element, float on, float off, bool initiallyVisible) where T : FrameworkElement
        {
            // Get the compositor
            Visual visual = await DispatcherHelper.GetFromUIThreadAsync(element.GetVisual);
            Compositor compositor = visual.Compositor;

            // Create the saturation effect and the effect factory
            SaturationEffect saturationEffect = new SaturationEffect
            {
                Name = "SEffect",
                Saturation = initiallyVisible ? off : on,
                Source = new CompositionEffectSourceParameter("source")
            };
            const String animationPropertyName = "SEffect.Saturation";
            CompositionEffectFactory effectFactory = compositor.CreateEffectFactory(saturationEffect, new[] { animationPropertyName });

            // Setup the rest of the effect
            CompositionEffectBrush effectBrush = effectFactory.CreateBrush();
            effectBrush.SetSourceParameter("source", compositor.CreateBackdropBrush());

            // Assign the effect to a brush and display it
            SpriteVisual sprite = compositor.CreateSpriteVisual();
            sprite.Brush = effectBrush;
            await DispatcherHelper.RunOnUIThreadAsync(() =>
            {
                // Adjust the sprite size
                sprite.Size = new Vector2((float)element.ActualWidth, (float)element.ActualHeight);

                // Set the child visual
                ElementCompositionPreview.SetElementChildVisual(element, sprite);
                if (initiallyVisible) element.Opacity = 1;
            });
            return new AttachedAnimatableCompositionEffect<T>(element, sprite, effectBrush, Tuple.Create(animationPropertyName, on, off));
        }

        /// <summary>
        /// Creates a new <see cref="AttachedAnimatableCompositionEffect{T}"/> instance for the target element
        /// </summary>
        /// <typeparam name="T">The type of element to blur</typeparam>
        /// <param name="element">The target element</param>
        /// <param name="on">The amount of blur effect to apply</param>
        /// <param name="off">The default amount of blur effect to apply</param>
        /// <param name="initiallyVisible">Indicates whether or not to apply the effect right away</param>
        [MustUseReturnValue, NotNull]
        public static async Task<AttachedAnimatableCompositionEffect<T>> GetAttachedAnimatableBlurEffectAsync<T>(
            [NotNull] this T element, float on, float off, bool initiallyVisible) where T : FrameworkElement
        {
            // Get the compositor
            Visual visual = await DispatcherHelper.GetFromUIThreadAsync(element.GetVisual);
            Compositor compositor = visual.Compositor;

            // Create the blur effect and the effect factory
            GaussianBlurEffect blurEffect = new GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = 0f,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("source")
            };
            const String animationPropertyName = "Blur.BlurAmount";
            CompositionEffectFactory effectFactory = compositor.CreateEffectFactory(blurEffect, new[] { animationPropertyName });

            // Setup the rest of the effect
            CompositionEffectBrush effectBrush = effectFactory.CreateBrush();
            effectBrush.SetSourceParameter("source", compositor.CreateBackdropBrush());

            // Assign the effect to a brush and display it
            SpriteVisual sprite = compositor.CreateSpriteVisual();
            sprite.Brush = effectBrush;
            await DispatcherHelper.RunOnUIThreadAsync(() =>
            {
                // Adjust the sprite size
                sprite.Size = new Vector2((float)element.ActualWidth, (float)element.ActualHeight);

                // Set the child visual
                ElementCompositionPreview.SetElementChildVisual(element, sprite);
                if (initiallyVisible) element.Opacity = 1;
            });
            return new AttachedAnimatableCompositionEffect<T>(element, sprite, effectBrush, Tuple.Create(animationPropertyName, on, off));
        }

        /// <summary>
        /// Creates a new <see cref="AttachedCompositeAnimatableCompositionEffect{T}"/> instance for the target element that
        /// applies both a blur and a saturation effect to the visual item
        /// </summary>
        /// <typeparam name="T">The type of element to blur</typeparam>
        /// <param name="element">The target element</param>
        /// <param name="onBlur">The amount of blur effect to apply</param>
        /// <param name="offBlur">The default amount of blur effect to apply</param>
        /// <param name="onSaturation">The amount of saturation effect to apply</param>
        /// <param name="offSaturation">The default amount of saturation effect to apply</param>
        /// <param name="initiallyVisible">Indicates whether or not to apply the effect right away</param>
        [MustUseReturnValue, NotNull]
        public static async Task<AttachedCompositeAnimatableCompositionEffect<T>> GetAttachedAnimatableBlurAndSaturationEffectAsync<T>(
            [NotNull] this T element, float onBlur, float offBlur, float onSaturation, float offSaturation, bool initiallyVisible) where T : FrameworkElement
        {
            // Get the compositor
            Visual visual = await DispatcherHelper.GetFromUIThreadAsync(element.GetVisual);
            Compositor compositor = visual.Compositor;

            // Create the blur effect, the saturation effect and the effect factory
            GaussianBlurEffect blurEffect = new GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = 0f,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("source")
            };
            SaturationEffect saturationEffect = new SaturationEffect
            {
                Name = "SEffect",
                Saturation = initiallyVisible ? offSaturation : onSaturation,
                Source = blurEffect
            };
            const String blurParameter = "Blur.BlurAmount", saturationParameter = "SEffect.Saturation";
            CompositionEffectFactory effectFactory = compositor.CreateEffectFactory(saturationEffect, new[]
            {
                blurParameter,
                saturationParameter
            });

            // Setup the rest of the effect
            CompositionEffectBrush effectBrush = effectFactory.CreateBrush();
            effectBrush.SetSourceParameter("source", compositor.CreateBackdropBrush());

            // Assign the effect to a brush and display it
            SpriteVisual sprite = compositor.CreateSpriteVisual();
            sprite.Brush = effectBrush;
            await DispatcherHelper.RunOnUIThreadAsync(() =>
            {
                // Adjust the sprite size
                sprite.Size = new Vector2((float)element.ActualWidth, (float)element.ActualHeight);

                // Set the child visual
                ElementCompositionPreview.SetElementChildVisual(element, sprite);
                if (initiallyVisible) element.Opacity = 1;
            });

            // Prepare and return the wrapped effect
            return new AttachedCompositeAnimatableCompositionEffect<T>(element, sprite, effectBrush,
                new Dictionary<String, Tuple<float, float>>
                {
                    { blurParameter, Tuple.Create(onBlur, offBlur) },
                    { saturationParameter, Tuple.Create(onSaturation, offSaturation) }
                });
        }

        #endregion
    }
}