﻿module FsMap3.Test

open Common


/// Tests some map generation components.
let testMap3Dna (seed : int) =

  let n = 1000
  let rnd = Rnd(seed)

  for i = 1 to n do

    let specimen = Specimen.generate(rnd, Map3Dna.generateEditorMap false)
    let predicate : ParameterPredicate = fun rnd dna i ->
      rnd.choose(2.0, Retain, 0.5, Jolt01 (rnd.float()), 0.5, Adjust01 (rnd.float(-1.0, 1.0)))
    let editor = InteractiveSource(rnd.tick, parameterPredicate = predicate)
    editor.observe(specimen.dna, rnd.float(1.0e6))
    let mutator = RecombinationSource(rnd.tick, parameterPredicate = predicate)
    mutator.observe(specimen.dna, rnd.float(1.0e6))
    let map1 = mutator.generate(Map3Dna.generateEditorMap false)
    let map2 = editor.generate(Map3Dna.generateEditorMap false)
    // Take some samples.
    for j = 1 to 100 do
      let v = map1.map (rnd.vec3f())
      enforce (v.isFinite) "FsMap3.Test.test: Non-finite map value from map generated by InteractiveSource."
      let v = map2.map (rnd.vec3f())
      enforce (v.isFinite) "FsMap3.Test.test: Non-finite map value from map generated by RecombinationSource."
    
    if i % 100 = 0 then printfn "%d/%d tested." i n

  printfn "OK."


(*
#r @"bin\Debug\FsMap3.dll"
FsMap3.Test.testMap3Dna 3
*)
