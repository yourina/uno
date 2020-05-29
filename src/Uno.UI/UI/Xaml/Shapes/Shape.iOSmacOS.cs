﻿using System;
using System.Linq;
using Windows.Foundation;
using Windows.UI.Xaml.Media;
using CoreAnimation;
using CoreGraphics;
using Uno.Extensions;
using Uno.Extensions.ValueType;
using Uno.Logging;
using Uno.UI;
using static System.Double;

#if __IOS__
using _Color = UIKit.UIColor;
#elif __MACOS__
using AppKit;
using _Color = AppKit.NSColor;
#endif

namespace Windows.UI.Xaml.Shapes
{
	partial class Shape
	{
		private CALayer _shapeLayer;

		public Shape()
		{
			// Background color is black by default, if and only if overriding Draw(CGRect rect).
#if __IOS__
			base.BackgroundColor = SolidColorBrushHelper.Transparent.Color;
#elif __MACOS__
			base.WantsLayer = true;
			base.Layer.BackgroundColor = SolidColorBrushHelper.Transparent.Color;
#endif
		}

		protected override void OnBackgroundChanged(DependencyPropertyChangedEventArgs e)
		{
			// Don't call base, we need to keep UIView.BackgroundColor set to transparent
		}

		#region Measure / Arrange should be shared using Geometry instead of CGPath
		private protected Size MeasureRelativeShape(Size availableSize)
		{
			var stretch = Stretch;
			var userMinSize = new Size(MinWidth, MinHeight);
			var userSize = new Size(Width, Height);

			var size = userSize;

			// If no user size defined on a given axis, we try to stretch along this axis
			if (IsNaN(size.Width))
			{
				size.Width = stretch == Stretch.UniformToFill || HorizontalAlignment == HorizontalAlignment.Stretch
					? availableSize.Width
					: 0;
			}
			if (IsNaN(size.Height))
			{
				size.Height = stretch == Stretch.UniformToFill || VerticalAlignment == VerticalAlignment.Stretch
					? availableSize.Height
					: 0;
			}

			// In case userSize was not defined, we still have to apply the min size
			size = size
				.AtLeast(userMinSize)
				.NumberOrDefault(userMinSize);

			if (IsInfinity(size.Width) || IsInfinity(size.Height))
			{
				// The size is invalid (the userSize was not defined and we were not able to stretch), just hide the shape.
				// Note: This will be overriden by the Layouter that will enforce the MinSize
				return default;
			}
			else
			{
				return size;
			}
		}
		private protected (Size shapeSize, Rect renderingArea) ArrangeRelativeShape(Size finalSize)
		{
			var horizontal = HorizontalAlignment;
			var vertical = VerticalAlignment;
			var stretch = Stretch;
			var userMinSize = new Size(MinWidth, MinHeight);
			var userMaxSize = new Size(MaxWidth, MaxHeight);
			var userSize = new Size(Width, Height);

			var size = userSize;

			// Like for the measure, if no user size defined on a given axis, we try to stretch along this axis
			if (IsNaN(size.Width))
			{
				size.Width = stretch == Stretch.UniformToFill || HorizontalAlignment == HorizontalAlignment.Stretch
					? finalSize.Width
					: 0;
			}
			if (IsNaN(size.Height))
			{
				size.Height = stretch == Stretch.UniformToFill || VerticalAlignment == VerticalAlignment.Stretch
					? finalSize.Height
					: 0;
			}

			// Like for the measure, in case userSize was not defined, we still have to apply the min size
			size = size
				.AtLeast(userMinSize)
				.NumberOrDefault(userMinSize);

			//// Once the specific stretching has been applied, we can compute the real 'userSize' which must also includes min and max.
			//// It will only be used for the alignment.
			//userSize = userSize
			//	.NumberOrDefault(userMaxSize)
			//	.AtMost(userMaxSize)
			//	.AtLeast(userMinSize); // UWP is applying "min" after "max", so if "min" > "max", "min" wins

			// The area that will be used to render the rectangle/ellipse as path
			var renderingArea = new Rect(new Point(), size);
			// This is the size that is use to align the shape. If the provided 
			//var renderingSize = size;

			// Apply the stretch mode, as it might change the "shape" of a "relative shape"
			switch (stretch)
			{
				case Stretch.None:
					renderingArea.Height = renderingArea.Width = 0;
					break;

				default:
				case Stretch.Fill:
					// size is already valid ... nothing to do!
					break;

				case Stretch.Uniform when renderingArea.Width < renderingArea.Height:
					renderingArea.Height = renderingArea.Width;
					break;

				case Stretch.Uniform: // when pathArea.Width >= pathArea.Height:
					renderingArea.Width = renderingArea.Height;
					break;

				case Stretch.UniformToFill when renderingArea.Width < renderingArea.Height:
					renderingArea.Width = renderingArea.Height;
					break;

				case Stretch.UniformToFill: // when pathArea.Width >= pathArea.Height:
					renderingArea.Height = renderingArea.Width;
					break;
			}

			// The path will be injected as a Layer, so we also have to apply the horizontal and vertical alignments
			// Note: We have to make this adjustment only if the shape is overflowing the container bounds,
			//		 otherwise the alignment will be correctly applied by the container.
			(bool horizontally, bool vertically) shouldAlign;
			switch (stretch)
			{
				case Stretch.UniformToFill:
					// Once the specific stretching has been applied, we can compute the real 'userSize' which must also includes min and max.
					// It will only be used for the alignment.
					//userSize = userSize
					//	.NumberOrDefault(userMaxSize)
					//	.AtMost(userMaxSize)
					//	.AtLeast(userMinSize); // UWP is applying "min" after "max", so if "min" > "max", "min" wins

					//if (IsNaN(userSize.Width))
					//{
					//	if (!IsInfinity(userMaxSize.Width))
					//	{
					//		userSize.Width = userMaxSize.Width;
					//	}
					//	else if (userMinSize.Width > 0)
					//	{
					//		userSize.Width = userMinSize.Width;
					//	}
					//}
					//if (IsNaN(userSize.Height))
					//{
					//	if (!IsInfinity(userMaxSize.Height))
					//	{
					//		userSize.Height = userMaxSize.Height;
					//	}
					//	else if (userMinSize.Height > 0)
					//	{
					//		userSize.Height = userMinSize.Height;
					//	}
					//}

					userSize = userSize
						.NumberOrDefault(userMaxSize)
						.AtLeast(userMinSize);

					// By default we align if UniformToFill, EXCEPT if the the userSize (or max, lowered by min) is lower than the finalSize
					// For reference, it's almost equivalent to:
					// var horizontally = IsNaN(userSize.Width) || (!IsInfinity(userSize.Width) && userSize.Width > finalSize.Width) || userMinSize.Width > 0;
					// shouldAlign = (horizontally || vertically, horizontally || vertically);
					var notHorizontally = userSize.Width <= finalSize.Width;
					var notVertically = userSize.Height <= finalSize.Height;

					shouldAlign = (!notHorizontally && !notVertically, !notHorizontally && !notVertically);
					break;

				default:
					// WinUI does not adjust alignment if the shape was smaller than the finalSize
					shouldAlign = (userSize.Width > finalSize.Width, userSize.Height > finalSize.Height);
					break;
			}


			var alignmentWidth = Math.Max(size.Width, renderingArea.Width);
			var horizontalOverflow = alignmentWidth - finalSize.Width;
			if (horizontalOverflow > 0 && shouldAlign.horizontally) 
			{
				switch (horizontal)
				{
					case HorizontalAlignment.Center:
						renderingArea.X -= horizontalOverflow / 2.0;
						break;

					case HorizontalAlignment.Right:
						renderingArea.X -= horizontalOverflow;
						break;
				}
			}
			var alignmentHeight = Math.Max(size.Height, renderingArea.Height);
			var verticalOverflow = alignmentHeight - finalSize.Height;
			if (verticalOverflow > 0 && shouldAlign.vertically)
			{
				switch (vertical)
				{
					case VerticalAlignment.Center:
						renderingArea.Y -= verticalOverflow / 2.0;
						break;

					case VerticalAlignment.Bottom:
						renderingArea.Y -= verticalOverflow;
						break;
				}
			}

			// For the Rectangle and the Ellipse, half of the StrokeThickness has to be excluded on each side of the shape.
			var twoHalfStrokeThickness = StrokeThickness;
			var halfStrokeThickness = twoHalfStrokeThickness / 2.0;
			renderingArea.X += halfStrokeThickness;
			renderingArea.Y += halfStrokeThickness;
			renderingArea.Width -= twoHalfStrokeThickness;
			renderingArea.Height -= twoHalfStrokeThickness;

			return (size, renderingArea);
		}

		private protected Size MeasureAbsoluteShape(Size availableSize, CGPath path)
		{
			if (path == null)
			{
				return default;
			}

			var stretch = Stretch;
			var (userMinSize, userMaxSize) = this.GetMinMax();
			var userSize = GetUserSizes();
			var strokeThickness = StrokeThickness;
			var halfStrokeThickness = GetHalfStrokeThickness();
			var pathBounds = path.BoundingBox;
			var pathSize = (Size)pathBounds.Size;

			if (nfloat.IsInfinity(pathBounds.Right) || nfloat.IsInfinity(pathBounds.Bottom))
			{
				if (this.Log().IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
				{
					this.Log().Debug($"Ignoring path with invalid bounds {pathBounds}");
				}

				return default;
			}

			// Compute the final size of the Shape and the render properties
			Size size;
			(double x, double y) renderScale;
			switch (stretch)
			{
				default:
				case Stretch.None:
					// If stretch is None, we have to keep the origin defined by the absolute coordinates of the path:
					//
					// This means that if you draw a line from 50,50 to 100,100 (so it's not starting at 0, 0),
					// with a 'None' stretch mode you will have:
					//    ------
					//    |    |
					//    |    |
					//    |  \ |
					//    |   \|
					//    ------
					//    
					// while with another Stretch mode you will have:
					//    ------
					//    |\   |
					//    | \  |
					//    |  \ |
					//    |   \|
					//    ------
					//
					// So for measure when None we includes that origin in the path size.
					//
					// Also, as the path does not have any notion of stroke thickness, we have to include it for the measure phase.
					// Note: The logic would say to include the full StrokeThickness as it will "overflow" half on booth side of the path,
					//		 but WinUI does include only the half of it.
					var pathNaturalSize = new Size(
						pathBounds.X == 0 ? pathBounds.Width + strokeThickness : pathBounds.Right + halfStrokeThickness,
						pathBounds.Y == 0 ? pathBounds.Height + strokeThickness : pathBounds.Bottom + halfStrokeThickness);
					size = pathNaturalSize.AtMost(userMaxSize).AtLeast(userMinSize); // The size defined on the Shape has priority over the size of the geometry itself!
					break;

				case Stretch.Fill:
					size = userMaxSize.FiniteOrDefault(availableSize);
					break;

#if !IS_DESIRED_SMALLER_THAN_MIN_ALLOWED
				case Stretch.Uniform when (userSize.min.hasWidth && userSize.min.width > availableSize.Width) || (userSize.min.hasHeight && userSize.min.height > availableSize.Height):
					size = availableSize;
					break;

				// Note: If the parent is going to stretch us due to the Width and/or the Height, we still go in the case below,
				//		 so we compute the effective size and we properly full-fill the _realDesiredSize
#endif

				case Stretch.Uniform:
					size = userMaxSize.FiniteOrDefault(availableSize);
					renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);
					if (renderScale.x > renderScale.y)
					{
						renderScale.x = renderScale.y;
						size.Width = pathSize.Width * renderScale.x + strokeThickness;
					}
					else
					{
						renderScale.y = renderScale.x;
						size.Height = pathSize.Height * renderScale.y + strokeThickness;
					}
					break;

				case Stretch.UniformToFill:
					size = userMinSize.AtLeast(availableSize);
					renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);
					if (renderScale.x < renderScale.y)
					{
						renderScale.x = renderScale.y;
						size.Width = pathSize.Width * renderScale.x + strokeThickness;
					}
					else
					{
						renderScale.y = renderScale.x;
						size.Height = pathSize.Height * renderScale.y + strokeThickness;
					}
					break;
			}

			_realDesiredSize = size;

			return size;
		}

		// The desired size before it has been changed to apply the [Min]<Width|Height>
		private Size _realDesiredSize;

		private protected Size ArrangeAbsoluteShape(Size finalSize, CGPath path)
		{
			if (path == null)
			{
				Render(null);
				return default;
			}

			var stretch = Stretch;
			//var (userMinSize, userMaxSize) = this.GetMinMax();
			var userSize = GetUserSizes();
			var strokeThickness = StrokeThickness;
			var halfStrokeThickness = GetHalfStrokeThickness();
			var pathBounds = path.BoundingBox;
			var pathSize = (Size)pathBounds.Size;
			var vertical = VerticalAlignment;
			var horizontal = HorizontalAlignment;

			if (nfloat.IsInfinity(pathBounds.Right) || nfloat.IsInfinity(pathBounds.Bottom))
			{
				if (this.Log().IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
				{
					this.Log().Debug($"Ignoring path with invalid bounds {pathBounds}");
				}

				return default;
			}

			// Compute the final size of the Shape and the render properties
			Size size;
			(double x, double y) renderOrigin, renderScale, renderOverflow;
			switch (stretch)
			{
				default:
				case Stretch.None:
					var pathNaturalSize = new Size(
						pathBounds.X == 0 ? pathBounds.Width + strokeThickness : pathBounds.Right + halfStrokeThickness,
						pathBounds.Y == 0 ? pathBounds.Height + strokeThickness : pathBounds.Bottom + halfStrokeThickness);
					var (userMinSize, userMaxSize) = this.GetMinMax();
					size = pathNaturalSize.AtMost(userMaxSize).AtLeast(userMinSize); // The size defined on the Shape has priority over the size of the geometry itself!
					renderScale = (1, 1);
					renderOrigin = (0, 0);
					renderOverflow = (size.Width - finalSize.Width, size.Height - finalSize.Height); // We do not add halfStrokeThickness: The stroke is allowed to flow out of container for None
					break;

				case Stretch.Fill:
					size = new Size(
						userSize.hasWidth
							? userSize.value.Width.AtMost(userSize.max.width)
							: userSize.max.width.AtMost(finalSize.Width).FiniteOrDefault(finalSize.Width),
						userSize.hasHeight
							? userSize.value.Height.AtMost(userSize.max.height)
							: userSize.max.height.AtMost(finalSize.Height).FiniteOrDefault(finalSize.Height));
					renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);
					renderOrigin = (halfStrokeThickness - pathBounds.X * renderScale.x, halfStrokeThickness - pathBounds.Y * renderScale.y);
					renderOverflow = (size.Width - finalSize.Width, size.Height - finalSize.Height);
					break;

				case Stretch.Uniform:
#if !IS_DESIRED_SMALLER_THAN_MIN_ALLOWED
					// Unlike WinUI, on Uno the layouter does not allow a FrameworkElement to return a size smaller than the [Min]<Width|Height> in its Measure
					// (it will forcefully apply the min size to the value returned by the Measure before storing it in the DesiredSize).
					// But when stretch is Uniform, if for instance the geometry is 100x100, the (min) size is 200x300 and the available size is 200x200,
					// then on WinUI the resulting shape (and its DesiredSize) will be 200x200, which is obviously smaller than the (min) size!
					// So here it's a workaround that will detect that specific case (isBeingStretchedByParent),
					// and then applies an offset to the rendering origin to compensate the wrong size used by the parent for alignment.

					var parentFinalSize = finalSize;
					var availableWhenStretchedForSize = _realDesiredSize;
					var availableWhenStretchedForMin = DesiredSize; // We use the desired size since it's the actual "available" size in that case.

					var isMinSizeForcefullyAppliedByParent = false;
					if (parentFinalSize.Width > availableWhenStretchedForMin.Width
						&& userSize.min.hasWidth && userSize.min.width == finalSize.Width)
					{
						isMinSizeForcefullyAppliedByParent = true;
						finalSize.Width = availableWhenStretchedForMin.Width;
					}
					else if (
						// It's not expected to be stretched but parent is trying to stretch us ...
						horizontal != HorizontalAlignment.Stretch && parentFinalSize.Width > availableWhenStretchedForSize.Width
						// ... and we do have a Width defined on us which is larger that the measured size.
						&& userSize.hasWidth && userSize.value.Width > availableWhenStretchedForSize.Width
						)
					{
						isMinSizeForcefullyAppliedByParent = true;
						finalSize.Width = availableWhenStretchedForSize.Width;
					}

					if (parentFinalSize.Height > availableWhenStretchedForMin.Height
						&& userSize.min.hasHeight && userSize.min.height == finalSize.Height)
					{
						isMinSizeForcefullyAppliedByParent = true;
						finalSize.Height = availableWhenStretchedForMin.Height;
					}
					else if (
						vertical != VerticalAlignment.Stretch && parentFinalSize.Height > availableWhenStretchedForSize.Height
						&& userSize.hasHeight && userSize.value.Height > availableWhenStretchedForSize.Height)
					{
						isMinSizeForcefullyAppliedByParent = true;
						finalSize.Height = availableWhenStretchedForSize.Height;
					}
#endif

					var defaultSize = size = new Size(
						userSize.hasWidth
							? userSize.value.Width.AtMost(userSize.max.width)
							: userSize.max.width.AtMost(finalSize.Width).FiniteOrDefault(finalSize.Width),
						userSize.hasHeight
							? userSize.value.Height.AtMost(userSize.max.height)
							: userSize.max.height.AtMost(finalSize.Height).FiniteOrDefault(finalSize.Height));
					//renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);

					// WARNING: We must do this comparison in that order to reproduce a bug on WinUI were a MinWidth larger than the finalSize.Width
					//			will be applied properly, BUT a MinHeight taller than the finalSize.Height will be ignored.
					//			(Note: When MinHeight ignored, shape is then be aligned according to the VerticalAlignment)
					//if (renderScale.y > renderScale.x) 
					//{
					//	renderScale.y = renderScale.x;
					//	size.Height = pathSize.Height * renderScale.y + strokeThickness;

					//	// Reproduces a bug of WinUI where the MinSize is constrained by the finalSize
					//	// Note: we should use userSize.min.height.AtMost(finalSize.Height), but the finalSize does contains the min size
					//	var minHeightForScale = userSize.min.height;//.AtMost(finalSize.Height); //_realDesiredSize.Height;

					//	if (userSize.min.hasHeight && size.Height < minHeightForScale)
					//	{
					//		// The current scale does not permit us to respect user's min size.
					//		// We restart the scale computation, but before we scale up the target render size so the computed scale will meet the requirements.
					//		// Note: We need to re-invoke the ComputeScaleFactors in order to properly apply StrokeThickness
					//		var adjustmentScale = minHeightForScale / size.Height;
					//		size = defaultSize.Multiply(adjustmentScale);
					//		renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);

					//		renderScale.y = renderScale.x;
					//		size.Height = pathSize.Height * renderScale.y + strokeThickness;
					//	}
					//}
					//else
					//{
					//	renderScale.x = renderScale.y;
					//	size.Width = pathSize.Width * renderScale.x + strokeThickness;

					//	// Reproduces a bug of WinUI where the MinSize is constrained by the finalSize
					//	// Note: we should use userSize.min.width.AtMost(finalSize.Width), but the finalSize does contains the min size
					//	var minWidthForScale = userSize.min.width;//.AtMost(finalSize.Width); //_realDesiredSize.Width;

					//	if (userSize.min.hasWidth && size.Width < minWidthForScale)
					//	{
					//		// The current scale does not permit us to respect user's min size.
					//		// We restart the scale computation, but before we scale up the target render size so the computed scale will meet the requirements.
					//		// Note: We need to re-invoke the ComputeScaleFactors in order to properly apply StrokeThickness
					//		var adjustmentScale = minWidthForScale / size.Width;
					//		size = defaultSize.Multiply(adjustmentScale);
					//		renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);

					//		renderScale.x = renderScale.y;
					//		size.Width = pathSize.Width * renderScale.x + strokeThickness;
					//	}
					//}

					//if (renderScale.x == renderScale.y)
					//{
					//	renderScale.x = renderScale.y;
					//	size.Width = pathSize.Width * renderScale.x + strokeThickness;

					//	// Reproduces a bug of WinUI where the MinSize is constrained by the finalSize
					//	// Note: we should use userSize.min.width.AtMost(finalSize.Width), but the finalSize does contains the min size
					//	var minWidthForScale = Math.Max(finalSize.Width, finalSize.Height);//.AtMost(finalSize.Width); //_realDesiredSize.Width;

					//	if (userSize.min.hasWidth && size.Width < minWidthForScale)
					//	{
					//		// The current scale does not permit us to respect user's min size.
					//		// We restart the scale computation, but before we scale up the target render size so the computed scale will meet the requirements.
					//		// Note: We need to re-invoke the ComputeScaleFactors in order to properly apply StrokeThickness
					//		var adjustmentScale = minWidthForScale / size.Width;
					//		size = defaultSize.Multiply(adjustmentScale);
					//		renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);

					//		renderScale.x = renderScale.y;
					//		size.Width = pathSize.Width * renderScale.x + strokeThickness;
					//	}
					//}
					//else if (renderScale.x > renderScale.y)
					//{
					//	renderScale.x = renderScale.y;
					//	size.Width = pathSize.Width * renderScale.x + strokeThickness;

					//	// Reproduces a bug of WinUI where the MinSize is constrained by the finalSize
					//	// Note: we should use userSize.min.width.AtMost(finalSize.Width), but the finalSize does contains the min size
					//	var minWidthForScale = userSize.min.width;//.AtMost(finalSize.Width); //_realDesiredSize.Width;

					//	if (userSize.min.hasWidth && size.Width < minWidthForScale)
					//	{
					//		// The current scale does not permit us to respect user's min size.
					//		// We restart the scale computation, but before we scale up the target render size so the computed scale will meet the requirements.
					//		// Note: We need to re-invoke the ComputeScaleFactors in order to properly apply StrokeThickness
					//		var adjustmentScale = minWidthForScale / size.Width;
					//		size = defaultSize.Multiply(adjustmentScale);
					//		renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);

					//		renderScale.x = renderScale.y;
					//		size.Width = pathSize.Width * renderScale.x + strokeThickness;
					//	}
					//}
					//else
					//{
					//	renderScale.y = renderScale.x;
					//	size.Height = pathSize.Height * renderScale.y + strokeThickness;

					//	// Reproduces a bug of WinUI where the MinSize is constrained by the finalSize
					//	// Note: we should use userSize.min.height.AtMost(finalSize.Height), but the finalSize does contains the min size
					//	var minHeightForScale = userSize.min.height;//.AtMost(finalSize.Height); //_realDesiredSize.Height;

					//	if (userSize.min.hasHeight && size.Height < minHeightForScale)
					//	{
					//		// The current scale does not permit us to respect user's min size.
					//		// We restart the scale computation, but before we scale up the target render size so the computed scale will meet the requirements.
					//		// Note: We need to re-invoke the ComputeScaleFactors in order to properly apply StrokeThickness
					//		var adjustmentScale = minHeightForScale / size.Height;
					//		size = defaultSize.Multiply(adjustmentScale);
					//		renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);

					//		renderScale.y = renderScale.x;
					//		size.Height = pathSize.Height * renderScale.y + strokeThickness;
					//	}
					//}

					// This is a complete non sense as we should normally just use userSize.min.width and userSize.min.height,
					// but the code below actually reproduces a bug of WinUI where the MinWidth and MinHeight are somehow
					// constrained by the layout slot ...
					// Note: This is only a replication of what we observed in the UI tests, and might have some flow in the logic.
					//		 Especially, we expect that the max vs. min applied on Width vs. Height is probably drove by the aspect ratio.
					var minSizeForScale = default(Size);
					if (userSize.min.hasWidth && userSize.min.hasHeight)
					{
						var min = Math.Min(userSize.min.width, userSize.min.height);
						minSizeForScale = new Size(min, min);
					}
					else if (userSize.min.hasWidth)
					{
						var max = Math.Min(userSize.min.width, Math.Max(finalSize.Width, finalSize.Height));
						minSizeForScale = new Size(max, max);
					}
					else if (userSize.min.hasHeight)
					{
						var min = Math.Min(userSize.min.height, Math.Min(finalSize.Width, finalSize.Height));
						minSizeForScale = new Size(min, min);
					}

					do
					{
						renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);

						if (renderScale.x >= renderScale.y)
						{
							renderScale.x = renderScale.y;
							size.Width = pathSize.Width * renderScale.x + strokeThickness;
						}
						else
						{
							renderScale.y = renderScale.x;
							size.Height = pathSize.Height * renderScale.y + strokeThickness;
						}

						// Reproduces a bug of WinUI where the MinSize is constrained by the finalSize
						// Note: we should use userSize.min.width.AtMost(finalSize.Width), but the finalSize does contains the min size
						//var minWidthForScale = userSize.min.width; //.AtMost(finalSize.Width); //_realDesiredSize.Width;
						//var minHeightForScale = userSize.min.height; //.AtMost(finalSize.Height); //_realDesiredSize.Height;

						//minWidthForScale = userSize.min.width.AtMost(Math.Max(finalSize.Width, finalSize.Height));
						//minHeightForScale = userSize.min.height.AtMost(finalSize.Height);

						if (userSize.min.hasWidth && size.Width < minSizeForScale.Width)
						{
							// The current scale does not permit us to respect user's min size.
							// We restart the scale computation, but before we scale up the target render size so the computed scale will meet the requirements.
							// Note: We need to re-invoke the ComputeScaleFactors in order to properly apply StrokeThickness
							var adjustmentScale = minSizeForScale.Width / size.Width;
							defaultSize = size = defaultSize.Multiply(adjustmentScale);
							renderScale.x = MinValue;
						}
						else if (userSize.min.hasHeight && size.Height < minSizeForScale.Height)
						{
							// The current scale does not permit us to respect user's min size.
							// We restart the scale computation, but before we scale up the target render size so the computed scale will meet the requirements.
							// Note: We need to re-invoke the ComputeScaleFactors in order to properly apply StrokeThickness
							var adjustmentScale = minSizeForScale.Height / size.Height;
							defaultSize = size = defaultSize.Multiply(adjustmentScale);
							renderScale.y = MinValue;
						}
					} while (renderScale.y != renderScale.x);

					renderOrigin = (halfStrokeThickness - pathBounds.X * renderScale.x, halfStrokeThickness - pathBounds.Y * renderScale.y);

#if IS_DESIRED_SMALLER_THAN_MIN_ALLOWED
					renderOverflow = (size.Width - finalSize.Width, size.Height - finalSize.Height);
#else
					//var available = isBeingStretchedByParent
					//	? parentFinalSize.AtMost(DesiredSize) // We use the desired size since it's the actual "available" size in that case.
					//	: finalSize;
					var available = finalSize;
					renderOverflow = (size.Width - available.Width, size.Height - available.Height);

					if (isMinSizeForcefullyAppliedByParent)
					{ 
						// The parent will use the min size to align this Shape, so here we offset the renderOrigin by the opposite value that is going to be applied.
						// Notes about the renderOverflow:
						//		* As the parent aligns us using the wrong size, we have to apply it by ourself for all alignments
						//		* For alignment stretch is will be applied by the standard code path below, so don't apply it here
						var overflowCorrection = (x: parentFinalSize.Width - finalSize.Width, y: parentFinalSize.Height - finalSize.Height);
						if (overflowCorrection.x > 0)
						{
							switch (HorizontalAlignment)
							{
								case HorizontalAlignment.Center when renderOverflow.x < 0: renderOrigin.x += (overflowCorrection.x - renderOverflow.x) / 2.0; break;
								case HorizontalAlignment.Right when renderOverflow.x < 0: renderOrigin.x += (overflowCorrection.x - renderOverflow.x); break;
								case HorizontalAlignment.Center: renderOrigin.x += overflowCorrection.x / 2.0; break;
								case HorizontalAlignment.Right: renderOrigin.x += overflowCorrection.x; break;
							}
						}

						if (overflowCorrection.y > 0)
						{
							switch (VerticalAlignment)
							{
								case VerticalAlignment.Center when renderOverflow.y < 0: renderOrigin.y += (overflowCorrection.y - renderOverflow.y) / 2.0; break;
								case VerticalAlignment.Bottom when renderOverflow.y < 0: renderOrigin.y += (overflowCorrection.y - renderOverflow.y); break;
								case VerticalAlignment.Center: renderOrigin.y += overflowCorrection.y / 2.0; break;
								case VerticalAlignment.Bottom: renderOrigin.y += overflowCorrection.y; break;
							}
						}
					}
#endif

					break;

				case Stretch.UniformToFill:
					(userMinSize, userMaxSize) = this.GetMinMax();
					size = userMinSize.AtLeast(finalSize);
					renderScale = ComputeScaleFactors(pathSize, ref size, strokeThickness);
					var unScaledSize = size;
					if (renderScale.x < renderScale.y)
					{
						renderScale.x = renderScale.y;
						size.Width = pathSize.Width * renderScale.x + strokeThickness;
					}
					else
					{
						renderScale.y = renderScale.x;
						size.Height = pathSize.Height * renderScale.y + strokeThickness;
					}
					renderOrigin = (halfStrokeThickness - pathBounds.X * renderScale.x, halfStrokeThickness - pathBounds.Y * renderScale.y);
					// Reproduces a bug of WinUI where it's the size without the stretch that is being used to compute the alignments below
					renderOverflow = (
						userSize.hasWidth ? unScaledSize.Width - finalSize.Width : size.Width - finalSize.Width,
						userSize.hasHeight ? unScaledSize.Height - finalSize.Height : size.Height - finalSize.Height
					);
					break;
			}

			// As the Shape is rendered as a Layer which does not take in consideration alignment (when size is larger than finalSize),
			// compute the offset to apply to the rendering layer.
			var renderCenteredByDefault = stretch != Stretch.None;
			if (renderOverflow.x > 0 && (!userSize.hasWidth || userSize.value.Width > finalSize.Width)) // WinUI does not adjust alignment if the shape was smaller than the finalSize
			{
				switch (HorizontalAlignment)
				{
					case HorizontalAlignment.Center:
						renderOrigin.x -= renderOverflow.x / 2.0;
						break;

					case HorizontalAlignment.Right:
						renderOrigin.x -= renderOverflow.x;
						break;
				}
			}
			else if (renderCenteredByDefault && renderOverflow.x < 0 && HorizontalAlignment == HorizontalAlignment.Stretch)
			{
				// It might happen that even stretched, the shape does not use all the finalSize width,
				// in that case it's centered by WinUI.
				renderOrigin.x -= renderOverflow.x / 2.0;
			}

			if (renderOverflow.y > 0 && (!userSize.hasHeight || userSize.value.Height > finalSize.Height)) // WinUI does not adjust alignment if the shape was smaller than the finalSize
			{
				switch (VerticalAlignment)
				{
					case VerticalAlignment.Center:
						renderOrigin.y -= renderOverflow.y / 2.0;
						break;

					case VerticalAlignment.Bottom:
						renderOrigin.y -= renderOverflow.y;
						break;
				}
			}
			else if (renderCenteredByDefault && renderOverflow.y < 0 && VerticalAlignment == VerticalAlignment.Stretch)
			{
				// It might happen that even stretched, the shape does not use all the finalSize height,
				// in that case it's centered by WinUI.
				renderOrigin.y -= renderOverflow.y / 2.0;
			}

			// Finally render the shape in a Layer
			var renderTransform = new CGAffineTransform(
				(nfloat)renderScale.x, 0,
				0, (nfloat)renderScale.y,
				(nfloat)renderOrigin.x, (nfloat)renderOrigin.y);
			var renderPath = new CGPath(path, renderTransform);

			Render(renderPath);

#if __IOS__
			// If the Shape does not have size defined, and natural size of the geometry is lower than the finalSize,
			// then we don't clip the shape!
			ClipsToBounds = stretch != Stretch.None
				|| userSize.hasWidth || userSize.max.hasWidth || userSize.hasHeight || userSize.max.hasHeight
				|| pathSize.Width > finalSize.Width || pathSize.Height > finalSize.Height;
#endif

			return size;
		}
		#endregion

		#region Rendering (Native)
		private protected void Render(CGPath path)
		{
			// Remove the old layer if any
			_shapeLayer?.RemoveFromSuperLayer();

			// Well ... nothing to do !
			if (path == null)
			{
				_shapeLayer = null;
				return;
			}

			_shapeLayer = CreateLayer(path);
			Layer.AddSublayer(_shapeLayer);
		}

		private CALayer CreateLayer(CGPath path)
		{
			var pathLayer = new CAShapeLayer()
			{
				Path = path,
				StrokeColor = (Stroke as SolidColorBrush)?.ColorWithOpacity ?? Colors.Transparent,
				LineWidth = (nfloat)ActualStrokeThickness,
			};

			switch (Fill)
			{
				case SolidColorBrush colorFill:
					pathLayer.FillColor = colorFill.ColorWithOpacity;
					break;

				case ImageBrush imageFill when TryCreateImageBrushLayers(imageFill, GetFillMask(path), out var imageLayer):
					pathLayer.FillColor = Colors.Transparent;
					pathLayer.AddSublayer(imageLayer);
					break;

				case LinearGradientBrush gradientFill:
					var gradientLayer = gradientFill.GetLayer(Frame.Size);
					gradientLayer.Frame = Bounds;
					gradientLayer.Mask = GetFillMask(path);
					gradientLayer.MasksToBounds = true;

					pathLayer.FillColor = Colors.Transparent;
					pathLayer.AddSublayer(gradientLayer);
					break;

				case null:
					pathLayer.FillColor = Colors.Transparent;
					break;

				default:
					Application.Current.RaiseRecoverableUnhandledException(new NotSupportedException($"The brush {Fill} is not supported as Fill for a {this} on this platform."));
					pathLayer.FillColor = Colors.Transparent;
					break;
			}

			if (StrokeDashArray != null)
			{
				var pattern = StrokeDashArray.Select(d => (global::Foundation.NSNumber)d).ToArray();

				pathLayer.LineDashPhase = 0; // Starting position of the pattern
				pathLayer.LineDashPattern = pattern;
			}

			return pathLayer;

			CAShapeLayer GetFillMask(CGPath mask)
				=> new CAShapeLayer
				{
					Path = mask,
					Frame = Bounds,
					// We only use the fill color to create the mask area
					FillColor = _Color.White.CGColor,
				};
		}

		private bool TryCreateImageBrushLayers(ImageBrush imageBrush, CAShapeLayer fillMask, out CALayer imageContainerLayer)
		{
			var uiImage = imageBrush.ImageSource.ImageData;
			if (uiImage == null)
			{
				imageContainerLayer = default;
				return false;
			}

			// This layer is the one we apply the mask on. It's the full size of the shape because the mask is as well.
			imageContainerLayer = new CALayer
			{
				Frame = new CGRect(0, 0, Bounds.Width, Bounds.Height),
				Mask = fillMask,
				MasksToBounds = true,
				BackgroundColor = new CGColor(0, 0, 0, 0),
			};

			// The ImageBrush.Stretch will tell us the SIZE of the image we need for the layer
			var aspectRatio = uiImage.Size.AspectRatio();
			CGSize imageSize;
			switch (imageBrush.Stretch)
			{
				case Stretch.None:
					imageSize = uiImage.Size;
					break;
				case Stretch.Uniform:
					var width = Math.Min(Bounds.Width, Bounds.Height * aspectRatio);
					var height = width / aspectRatio;
					imageSize = new CGSize(width, height);
					break;
				case Stretch.UniformToFill:
					width = Math.Max(Bounds.Width, Bounds.Height * aspectRatio);
					height = width / aspectRatio;
					imageSize = new CGSize(width, height);
					break;
				default: // Fill
					imageSize = Bounds.Size;
					break;
			}

			// The ImageBrush.AlignementX/Y will tell us the LOCATION we need for the layer
			double deltaX;
			switch (imageBrush.AlignmentX)
			{
				case AlignmentX.Left:
					deltaX = 0;
					break;
				case AlignmentX.Right:
					deltaX = (double)(Bounds.Width - imageSize.Width);
					break;
				default: // Center
					deltaX = (double)(Bounds.Width - imageSize.Width) * 0.5f;
					break;
			}

			double deltaY;
			switch (imageBrush.AlignmentY)
			{
				case AlignmentY.Top:
					deltaY = 0;
					break;
				case AlignmentY.Bottom:
					deltaY = (double)(Bounds.Height - imageSize.Height);
					break;
				default: // Center
					deltaY = (double)(Bounds.Height - imageSize.Height) * 0.5f;
					break;
			}

			var imageFrame = new CGRect(new CGPoint(deltaX, deltaY), imageSize);

			// This is the layer with the actual image in it. Its frame is the inside of the border.
			var imageLayer = new CALayer
			{
				Contents = uiImage.CGImage,
				Frame = imageFrame,
				MasksToBounds = true,
			};

			imageContainerLayer.AddSublayer(imageLayer);

			return true;
		}
		#endregion

		#region Helper methods
		/// <summary>
		/// Gets the rounded/adjusted half stroke thickness that should be used for measuring absolute shapes (Path, Line, Polyline and Polygon)
		/// </summary>
		private double GetHalfStrokeThickness()
			=> Math.Floor((ActualStrokeThickness + .5) / 2.0);

		private (Size value, bool hasWidth, bool hasHeight) GetUserSize()
		{
			var width = Width;
			var height = Height;
			return (new Size(width, height), !IsNaN(width), !IsNaN(height));
		}

		private
			(
				Size value, bool hasWidth, bool hasHeight,
				(double width, bool hasWidth, double height, bool hasHeight) min,
				(double width, bool hasWidth, double height, bool hasHeight) max
			)
			GetUserSizes()
		{
			var width = Width;
			var height = Height;
			var minWidth = MinWidth;
			var minHeight = MinHeight;
			var maxWidth = MaxWidth.AtLeast(minWidth); // UWP is applying "min" after "max", so if "min" > "max", "min" wins
			var maxHeight = MaxHeight.AtLeast(minHeight);

			//minSize = size
			//	.NumberOrDefault(new Size(0, 0))
			//	.AtMost(maxSize)
			//	.AtLeast(minSize); // UWP is applying "min" after "max", so if "min" > "max", "min" wins

			//maxSize = size
			//	.NumberOrDefault(new Size(PositiveInfinity, PositiveInfinity))
			//	.AtMost(maxSize)
			//	.AtLeast(minSize); // UWP is applying "min" after "max", so if "min" > "max", "min" wins

			return (
				new Size(width, height), !IsNaN(width), !IsNaN(height),
				(minWidth, IsFinite(minWidth) && minWidth > 0, minHeight, IsFinite(minHeight) && minHeight > 0),
				(maxWidth, IsFinite(maxWidth), maxHeight, IsFinite(maxHeight)));
		}

		private static (float x, float y) ComputeScaleFactors(Size geometrySize, ref Size renderSize, double strokeThickness)
		{
			float x, y;
			if (geometrySize.Width < double.Epsilon)
			{
				x = 1;
				renderSize.Width = strokeThickness;
			}
			else
			{
				x = (float)((renderSize.Width - strokeThickness) / geometrySize.Width);
			}
			if (geometrySize.Height < double.Epsilon)
			{
				y = 1;
				renderSize.Height = strokeThickness;
			}
			else
			{
				y = (float)((renderSize.Height - strokeThickness) / geometrySize.Height);
			}

			return (x, y);
		}
		#endregion
	}
}
