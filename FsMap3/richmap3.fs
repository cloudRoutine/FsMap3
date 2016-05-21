﻿namespace FsMap3

open Common
open Basis3
open Map3
open Map3Info


/// A "rich" Map3 stores a 2-window for displaying an image and sampled information
/// for normalization and filtering.
[<NoEquality; NoComparison>]
type RichMap3 =
  {
    /// The map, normalized and colored, with the view window transformed to the XY unit square at Z = 0.
    map : Map3
    /// Palette transform.
    palette : Map3
    /// Map view center.
    center : Vec3f
    /// Map view zoom.
    zoom : float32
    /// View aspect ratio. TODO: This should be handled by the layout.
    aspectRatio : float32
    /// Info that was gathered from uncolored map during normalization. Can be useful for filtering.
    info : Map3Info
  }

  member inline this.viewWidth = 1.0f / this.zoom

  member inline this.viewHeight = 1.0f / this.zoom

  member this.viewBox =
    let w = this.viewWidth
    let h = this.viewHeight
    (this.center.x - w * 0.5f, this.center.y - h * 0.5f, this.center.x + w * 0.5f, this.center.y + h * 0.5f)

  member this.camera = fun x y -> Vec3f(x, y, 0.0f)

  static member inline pixmapCamera(w, h, x, y) =
    let Z = 1.0f / float32 (max w h)
    Vec3f(float32 x * Z, float32 y * Z, 0.0f)

  static member pixmapSourceWith(extraTransform : Vec3f -> Vec3f) =
    fun (rich : RichMap3) ->
      { new IPixmapSource with
        member __.start(w, h) = ()
        member __.getPixel(w, h, x, y) = rich.map (RichMap3.pixmapCamera(w, h, x, y) |> extraTransform) * 0.5f + Vec3f(0.5f)
        member __.finish() = ()
        member __.postFx(pixmap) = ()
      }

  static member pixmapSource =
    fun (rich : RichMap3) ->
      { new IPixmapSource with
        member __.start(w, h) = ()
        member __.getPixel(w, h, x, y) = rich.map (RichMap3.pixmapCamera(w, h, x, y)) * 0.5f + Vec3f(0.5f)
        member __.finish() = ()
        member __.postFx(pixmap) = ()
      }

  /// Generates a RichMap3. The palette and 2-window are generated and applied separately here.
  /// The map generator is supplied as an argument.
  static member generate(mapGenerator : Dna -> Map3) = fun (dna : Dna) ->
    let clerp = lerp -50.0f 50.0f
    let centerX = dna.float32("View Center X", clerp)
    let centerY = dna.float32("View Center Y", clerp)
    let centerZ = dna.float32("View Center Z", clerp)
    let center = Vec3f(centerX, centerY, centerZ)
    let zoom = dna.float32("View Zoom", xerp 0.5e-2f 0.5e3f)
    let offset = Vec3f(centerX - 0.5f / zoom, centerY - 0.5f / zoom, centerZ)
    let aspectRatio = 1.0f
    let palette = dna.descend("Palette", ColorDna.genPalette 32)
    let viewTransform (v : Vec3f) = v / zoom + offset
    let info = Map3Info.create(mapGenerator, dna, retainSamples = true, computeDeviation = true, computeSlopes = true)
    {
      RichMap3.map = viewTransform >> info.map >> palette
      palette = palette
      center = center
      zoom = zoom
      aspectRatio = aspectRatio
      info = info
    }

    

/// Applies some statistical filters to a generated map.
[<NoEquality; NoComparison>]
type RichMap3Filter =
  {
    mutable minSlope : float32
    mutable maxSlope : float32
    mutable minDeviation : float32
    mutable minDifference : float32
    mutable maxDifference : float32
  }

  // Returns whether the map passes the filters.
  member this.filter(map : RichMap3, map0 : RichMap3 option) =
    let viewSize = max map.viewWidth map.viewHeight

    let slope99Min = min (this.minSlope / viewSize) (map0.map(fun previous -> previous.info.slope99 >? infinityf) >? infinityf)
    let slope99Max = max (this.maxSlope / viewSize) (map0.map(fun previous -> previous.info.slope99 >? 0.0f) >? 0.0f)
    let deviationMin = min this.minDeviation (map0.map(fun previous -> previous.info.deviation >? infinityf) >? infinityf)

    if !map.info.slope99 < slope99Min || !map.info.slope99 > slope99Max then
      Log.infof "Map detail level rejected: 99%% slope minimum %d | actual %d | maximum %d" (int slope99Min) (int !map.info.slope99) (int slope99Max)
      false
    elif !map.info.deviation < deviationMin then
      Log.infof "Map sample deviation too small: deviation minimum %.2f | actual %.2f" deviationMin !map.info.deviation
      false
    else
      match map0 with
      | Some map0 ->
        let sample0 = map0.info.sampleArray
        let sample1 = map.info.sampleArray
        let n = min sample0.size sample1.size
        if n > 0 && map.info.sampleDiameter = map0.info.sampleDiameter then
          let difference = Fun.sum 0 (n - 1) (fun i -> (sample0.[i] - sample1.[i]).norm1) / float32 n
          if difference < this.minDifference then
            Log.infof "Map is too similar: difference %.4f" difference
            false
          elif difference > this.maxDifference then
            Log.infof "Map is too dissimilar: difference %.4f" difference
            false
          else
            Log.infof "Map accepted: 99%% slope %d | deviation %.2f | difference %.4f" (int !map.info.slope99) !map.info.deviation difference
            true
        else
          Log.infof "Map accepted: 99%% slope %d | deviation %.2f" (int !map.info.slope99) !map.info.deviation
          true
      | None ->
        Log.infof "Map accepted: 99%% slope %d | deviation %.2f" (int !map.info.slope99) !map.info.deviation
        true

