﻿// Mixing operators.
namespace FsMap3

open Common


/// MixState contains the current state of a mixing operator.
[<NoComparison>]
type MixState = struct
  /// w is extra state, typically a weight.
  val w : float32
  /// c is extra state, typically a "comparand" value.
  val c : Vec3f
  /// a is the current result.
  val a : Vec3f

  new(w : float32, c : Vec3f, a : Vec3f) = { w = w; c = c; a = a }
  new(w : float32, a : Vec3f) = { w = w; c = Vec3f.zero; a = a }
  new(a : Vec3f) = { w = 0.0f; c = Vec3f.zero; a = a }
end



/// Many Map3 functions support a mix operator, which governs how the influence of different features
/// is composited into the final result. The parameters are state -> w -> b -> result (w in ]0, 1]).
/// (Zero is not a valid weight but the mix operators are still required to handle it gracefully.)
[<NoEquality; NoComparison>]
type MixOp = MixState -> float32 -> Vec3f -> MixState


(*
A prescription for smooth mixing with normalized weights.

state.w = W = current weight in [0, 1]
state.a = a = current result weighted by state.w
a / W = current unweighted result

When mixing in w and b:

Combination weight: W * w
Empty weight: (1 - W) * (1 - w)
Unmodified a weight: W * (1 - w)
Unmodified b weight: (1 - W) * w

The combination weight is processed using the operator's mixing process f, which works with unweighted inputs
and returns an unweighted result. The complete formula is:

W' = 1 - (1 - W) * (1 - w)
a' = (1 - w) * a + (1 - W) * w * b + W * w * f(W, a / W, b, w)
*)


[<RequireQualifiedAccess>]
module Mix =

  /// Smooth normalized mixing helper for operators that do not need MixState.c.
  let inline normalizedWeightMix (f : float32 -> Vec3f -> float32 -> Vec3f -> Vec3f) (state : MixState) (w : float32) (b : Vec3f) =
    let W = state.w
    let m = if W > 0.0f then W * w * f W (state.a / W) w b else Vec3f.zero
    MixState(1.0f - (1.0f - W) * (1.0f - w), (1.0f - w) * state.a + (1.0f - W) * w * b + m)

  let start = MixState()

  /// The result of mixing is always stored in MixState.a. Many operators use nothing but MixState.a,
  /// not needing the extra state.
  let inline result (state : MixState) = state.a

  let layer (width : float32) (fade : float32 -> float32) (persist : float32) (state : MixState) (w : float32) (b : Vec3f) =
    // Lw = layering weight.
    let Lw = state.w
    if Lw > 0.0f then
      // Nw = non-layering weight.
      let Nw = 1.0f - state.w
      // bw = weight of b in layering.
      let bw = w * fade (max 0.0f (1.0f - (b - state.c).absNorm * 0.2f / width))
      // w' = new weight.
      let w' = min 1.0f (Lw + w)
      // Pw = persist weight of b.
      let Pw = (1.0f - persist) * w
      // In layering state, MixState.c is weighted by MixState.w.
      let c' = Lw * ((1.0f - Pw) * state.c / Lw + Pw * b) + Nw * w * b
      MixState(w', c', Lw / (1.0f + bw) * (state.a / Lw + bw * b) + Nw * w * b)
    else
      MixState(w, w * b, w * b)
   
  let sum (state : MixState) (w : float32) (b : Vec3f) = MixState(state.a + w * b)

  let over (state : MixState) (w : float32) (b : Vec3f) = MixState(lerp state.a b w)

  let soft (a : float32) (state : MixState) (w : float32) (b : Vec3f) =
    let inline mix x y = let wx, wy = qesp(a * x), qesp(a * y) in (x * wx + y * wy) / (wx + wy)
    normalizedWeightMix (fun _ a _ b -> Vec3f.bimap(a, b, mix)) state w b

  /// Mixes colors according to their squared norms with mixing hardness in [0, 1].
  let norm (hardness : float32) (state : MixState) (w : float32) (b : Vec3f) =
    // This is the simplest weight response that starts linearly and keeps accelerating.
    // Unlike exp, it does not work with negative arguments.
    // We just need to tweak the hardness transformation to get the desired response curve.
    let inline f x = 1.0f + x + squared x
    let h = squared hardness * 2.0f
    let aw = state.w * f(h * state.a.length2)
    let bw = w * f(h * b.length2)
    let w' = min 1.0f (state.w + w)
    MixState(w', (aw * state.a + bw * w * b) / (aw + bw + 1.0e-10f))

  let min (state : MixState) (w : float32) (b : Vec3f) = MixState(Vec3f.minimize state.a (w * b))

  let max (state : MixState) (w : float32) (b : Vec3f) = MixState(Vec3f.maximize state.a (w * b))


