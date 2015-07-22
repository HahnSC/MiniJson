// ----------------------------------------------------------------------------------------------
// Copyright 2015 Mårten Rånge
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Text

open MiniJson
open MiniJson.JsonModule
open MiniJson.DynamicJsonModule
open MiniJson.Tests.Test
open MiniJson.Tests.TestCases
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let jsonAsString (random : Random) (json : Json) : string =
  let sb = StringBuilder ()

  let inline str (s : string)     = ignore <| sb.Append s
  let inline ch  (c : char)       = ignore <| sb.Append c
  let inline num (f : float)      = ignore <| sb.AppendFormat (CultureInfo.InvariantCulture, "{0}", f)
  let ws ()                       =
    let e = random.Next(-1,3)
    for i = 0 to e do
      match random.Next(0,6) with
      | 0 -> ch '\n'
      | 1 -> ch '\r'
      | 2 -> ch '\t'
      | _ -> ch ' '

  let estr (s : string) =
    ch '"'
    let e = s.Length - 1
    for i = 0 to e do
      match s.[i] with
      | '\"'  -> str @"\"""
      | '\\'  -> str @"\\"
      | '/'   -> str @"\/"
      | '\b'  -> str @"\b"
      | '\f'  -> str @"\f"
      | '\n'  -> str @"\n"
      | '\r'  -> str @"\r"
      | '\t'  -> str @"\t"
      | c     -> ch c
    ch '"'

  let values b e (vs : 'T array) (a : 'T -> unit) =
    ch b
    ws ()
    let ee = vs.Length - 1
    for i = 0 to ee do
      let v = vs.[i]
      a v
      if i < ee then
        ch ','
        ws ()
    ch e
    ws ()

  let rec impl j =
    match j with
    | JsonNull          -> str "null"               ; ws ()
    | JsonBoolean true  -> str "true"               ; ws ()
    | JsonBoolean false -> str "false"              ; ws ()
    | JsonNumber n      -> num n                    ; ws ()
    | JsonString s      -> estr s                   ; ws ()
    | JsonArray vs      -> values '[' ']' vs impl   ; ws ()
    | JsonObject ms     -> values '{' '}' ms implkv ; ws ()
  and implkv (k,v) =
    estr k
    ws ()
    ch ':'
    ws ()
    impl v

  ws ()
  impl json

  sb.ToString ()
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let randomizeJson (n : int) (random : Random) : Json =
  let randomizeRawString  (n : int) : string =
    String (Array.init (random.Next (3, 10)) (fun _ -> char (random.Next(65,65+25))))

  let randomizeNull       (n : int) : Json = JsonNull
  let randomizeBool       (n : int) : Json = JsonBoolean (random.Next (0,2) = 1)
  let randomizeNumber     (n : int) : Json = JsonNumber (random.NextDouble () * 100000.)
  let randomizeNumber     (n : int) : Json = JsonNumber 1.
  let randomizeString     (n : int) : Json = JsonString (randomizeRawString (n - 1))
  let rec randomizeArray  (n : int) : Json =
    let vs = Array.init (random.Next (0, 5)) (fun _ -> randomize (n - 1))
    JsonArray vs
  and randomizeObject     (n : int) : Json =
    let ms = Array.init (random.Next (0, 5)) (fun _ -> randomizeRawString (n - 1), randomize (n - 1))
    JsonObject ms
  and randomize           (n : int) : Json =
    if n = 0 then randomizeNumber n
    else
      match random.Next(0,12) with
      | 0 | 1     -> randomizeNull n
      | 2 | 3     -> randomizeBool n
      | 4 | 5 | 6 -> randomizeNumber n
      | 7 | 8 | 9 -> randomizeString n
      | 10        -> randomizeArray n
      | _         -> randomizeObject n

  match random.Next(0,2) with
  | 0         -> randomizeArray n
  | _         -> randomizeObject n
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let compareParsers
  (referenceParser  : string -> ParseResult   )
  (positive         : bool                    )
  (name             : string                  )
  (testCase         : string                  )
  (postProcess      : Json -> Json -> unit    ) : unit =
  let expected  = referenceParser testCase
  let actual    = parse false testCase

  match expected, actual with
  | Success e     , Success a     ->
    check_eq true  positive  name
    if test_eq e a name then postProcess e a
  | Failure (_,e) , Failure (_,a) ->
    check_eq false positive  name
    check_eq e     a         name
  | _             , _             ->
    test_failuref "Parsing mismatch '%s', expected:%A, actual: %A" name expected actual
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let random = Random 19740531
let generatedTestCases = Array.init 1000 <| fun i ->
  randomizeJson 10 random |> fun json -> true, sprintf "Generated: %d" i,toString false json
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let runFunctionalTestCases
  (category       : string                                                      )
  (parserComparer : bool -> string -> string -> (Json -> Json ->  unit) -> unit )
  (testCases      : (bool*string*string) []                                     )
  (dumper         : string -> unit                                              ) : unit =
  let noAction _ _ = ()

  for positive, name, testCase in testCases do
    dumper <| sprintf "---==> %s <==---" category
    dumper name
    dumper (if positive then "positive" else "negative")
    dumper testCase

    parserComparer positive name testCase <| fun e a ->
      let unindented  = toString false e
      let indented    = toString true e
      let dumped      = jsonAsString random e

      dumper unindented
      dumper indented
      dumper dumped

      parserComparer positive name unindented noAction
      parserComparer positive name indented   noAction
      parserComparer positive name dumped     noAction
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let functionalTestCases (dumper : string -> unit) =
  let testCases = Array.concat [|positiveTestCases; negativeTestCases; sampleTestCases; generatedTestCases|]

  infof "Running %d functional testcases (REFERENCE)..." testCases.Length

  runFunctionalTestCases
    "FUNCTIONAL TEST (REFERENCE)"
    (compareParsers ReferenceJsonModule.parse)
    testCases
    dumper
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let filterForJsonNet (_,name,_) =
  match name with
  | "Sample: Dates.json"                  -> false  // JSON.NET parses dates, MiniJson doesn't (as JSON has no concept of Dates)
  | "Sample: GitHub.json"                 -> false  // JSON.NET fails to parse GitHub.Json (valid according to http://jsonlint.com)
  | "Sample: optionals.json"              -> false  // JSON.NET fails to parse optionals.Json (valid according to http://jsonlint.com)
  | _ when name.StartsWith ("Negative: ") -> false  // JSON.NET is more relaxed when parsing therefore negative testcases can't be compared
  | _ -> true
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let functionalJsonNetTestCases (dumper : string -> unit) =
  let testCases =
    Array.concat [|positiveTestCases; negativeTestCases; sampleTestCases; generatedTestCases |]
    |> Array.filter filterForJsonNet

  infof "Running %d functional testcases (JSON.NET)..." testCases.Length

  runFunctionalTestCases
    "FUNCTIONAL TEST (JSON.NET)"
    (compareParsers MiniJson.Tests.JsonNet.parse)
    testCases
    dumper
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let filterForFSharpData (_,name,_) =
  match name with
  | "Sample: optionals.json"              -> false  // FSharp.Data difference with MiniJson most likely due to Date/Float parsing, TODO: investigate
  | _ when name.StartsWith ("Negative: ") -> false  // FSharp.Data is more relaxed when parsing therefore negative testcases can't be compared
  | _ -> true
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let functionalFSharpDataTestCases (dumper : string -> unit) =
  let testCases =
    Array.concat [|positiveTestCases; negativeTestCases; sampleTestCases; generatedTestCases |]
    |> Array.filter filterForFSharpData

  infof "Running %d functional testcases (FSHARP.DATA)..." testCases.Length

  runFunctionalTestCases
    "FUNCTIONAL TEST (FSHARP.DATA)"
    (compareParsers MiniJson.Tests.FSharpData.parse)
    testCases
    dumper
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let filterForPerformance (_,name : string ,tc : string) =
  match name with
  | _ when name.StartsWith ("Negative: ") -> false  // Negative test cases aren't tested for performance
  | "Sample: contacts.json"               -> false  // contacts.json doesn't have enough complexity
  | _ -> tc.Length > 500                            // Allow all other JSON documents bigger than 500 char
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
type PerformanceData = string*int*int64

let collectPerformanceData
  (category       : string                  )
  (parser         : string -> ParseResult   )
  (iterations     : int                     )
  (testCases      : (bool*string*string) [] )
  (dumper         : string -> unit          ) : PerformanceData [] =

  infof "Collecting performance data for %d testcases (%s)..." testCases.Length category

  let sw = Stopwatch ()

  let timeIt n (a : unit -> 'T) : int64*'T =
    sw.Reset ()

    let result = a ()

    sw.Start ()

    for i = 1 to n do
      ignore <| a ()

    sw.Stop ()

    sw.ElapsedMilliseconds, result

  [|
    for positive, name, testCase in testCases do
      dumper <| sprintf "---==> PERFORMANCE (%s) <==---" category
      dumper name
      dumper (if positive then "positive" else "negative")
      dumper testCase

      let time , _ = timeIt iterations (fun _ -> parser testCase)
//      let actual    , _ = timeIt iterations (fun _ -> parse false testCase)

//      let actual_ratio  = float reference / max (float actual) 1.

//      let ref           = float reference / float iterations
//      let act           = float actual / float iterations

//      check_lt expected_ratio     actual_ratio            name
//      check_gt ref                (expected_ratio * act)  name

      dumper <| sprintf "Iterations: %d, time: %d ms" iterations time
      yield name, iterations, time
  |]
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let performanceRatio v = max 10.0 v
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let performanceTestCases (dumper : string -> unit) =
  let allTtestCases =
    Array.concat [|positiveTestCases; negativeTestCases; sampleTestCases; generatedTestCases |]

  let miniJsonData =
    let testCases = 
      allTtestCases 
      |> Array.filter filterForPerformance

    collectPerformanceData
      "MINIJSON"
      (parse false)
      1000
      testCases
      dumper
 
  let expectedRatio v = max 10.0 v

  let compareResults
    (name             : string            )
    (performanceRatio : float             )
    (data             : PerformanceData[] ) =
    infof "Comparing performance data between MINIJSON and %s" name

    for testCase0, iterations0, time0 in miniJsonData do
      match data |> Array.tryFind (fun (testCase1, _, _) -> testCase0 = testCase1) with
      | None -> ()
      | Some (_, iterations1, time1) ->
        let adjustedTime0   = float time0 / float iterations0
        let adjustedTime1   = float time1 / float iterations1

        let ratio           = adjustedTime1 / adjustedTime0

        check_lt (expectedRatio performanceRatio)   ratio         testCase0
        check_gt adjustedTime1                      adjustedTime0 testCase0

  let referenceData =
    let testCases =
      allTtestCases
      |> Array.filter filterForPerformance

    collectPerformanceData
      "REFERENCE"
      ReferenceJsonModule.parse
      200
      testCases
      dumper

  compareResults "REFERENCE" 4.0 referenceData

  let jsonNetData =
    let testCases =
      allTtestCases
      |> Array.filter filterForJsonNet
      |> Array.filter filterForPerformance

    collectPerformanceData
      "JSON.NET"
      MiniJson.Tests.JsonNet.parse
      1000
      testCases
      dumper

  compareResults "JSON.NET" 1.1 jsonNetData

  let fsharpDataData =
    let testCases =
      allTtestCases
      |> Array.filter filterForFSharpData
      |> Array.filter filterForPerformance

    collectPerformanceData
      "FSHARP.DATA"
      MiniJson.Tests.FSharpData.dummyParse
      1000
      testCases
      dumper

  compareResults "FSHARP.DATA" 1.5 fsharpDataData

  ()
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let errorReportingTestCases (dumper : string -> unit) =
  let testCases = Array.zip negativeTestCases errorReportingOracles

  infof "Running %d error reporting testcases..." testCases.Length

  for (positive, name, testCase), expected in testCases do
    dumper "---==> ERROR REPORTING <==---"
    dumper name
    dumper (if positive then "positive" else "negative")
    dumper testCase
    match parse true testCase with
    | Success v           -> test_failuref "Parsing expected to fail for '%s' : %A" name v
    | Failure (actual, _)  ->
      check_eq expected actual name
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let toStringTestCases (dumper : string -> unit) =
  let testCases = Array.zip3 positiveTestCases noIndentOracles withIndentOracles

  infof "Running %d toString testcases..." testCases.Length

  for (positive, name, testCase), noIndentOracle ,withIndentOracle in testCases do
    dumper "---==> TO STRING <==---"
    dumper name
    dumper (if positive then "positive" else "negative")
    dumper testCase
    match parse false testCase with
    | Success v       ->
      check_eq noIndentOracle   (v.ToString ()    ) name
      check_eq noIndentOracle   (toString false v ) name
      check_eq withIndentOracle (toString true v  ) name

    | Failure (_, _)  ->
      test_failuref "Parsing expected to succeed for '%s'" name
// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
let scalarToStringTestCases (dumper : string -> unit) =
  infof "Running Scalar ToString testcases..."

  // Tests that toString on scalar values (like null) generate valid JSON documents

  let testCases =
    [|
      JsonNull            , """[null]"""
      JsonBoolean false   , """[false]"""
      JsonBoolean true    , """[true]"""
      JsonNumber  123.    , """[123]"""
      JsonString  "Hello" , """["Hello"]"""
    |]

  for testCase, expected in testCases do
    check_eq expected (toString false testCase) expected

// ----------------------------------------------------------------------------------------------
let pathTestCases (dumper : string -> unit) =
  infof "Running Dynamic JSON testcases..."

  let test_scalar e a =
    match e,a with
    | ScalarNull        _     , ScalarNull        _                 -> true
    | ScalarBoolean     (_,a) , ScalarBoolean     (_,b) when a = b  -> true
    | ScalarNumber      (_,a) , ScalarNumber      (_,b) when a = b  -> true
    | ScalarString      (_,a) , ScalarString      (_,b) when a = b  -> true
    | ScalarNotScalar   _     , ScalarNotScalar   _                 -> true
    | ScalarInvalidPath _     , ScalarInvalidPath _                 -> true
    | _                       , _                                   ->
      errorf "TEST_SCALAR: %A equiv %A" e a
      false

  let check_scalar e a = ignore <| test_scalar e a

  let jsonArray =
    JsonArray
      [|
        JsonNull
        JsonBoolean true
        JsonNumber  0.
        JsonString  "Hello"
      |]

  let jsonObject =
    JsonObject
      [|
        "Null"    , JsonNull
        "Boolean" , JsonBoolean false
        "Number"  , JsonNumber  123.
        "String"  , JsonString  "There"
        "Array"   , jsonArray
      |]

  let rootObject =
    JsonObject
      [|
        "Object"    , jsonObject
        "Array"     , jsonArray
      |]


  let defaultPath       = JsonNull, []
  let defaultNull       = ScalarNull defaultPath
  let defaultBoolean  v = ScalarBoolean (defaultPath, v)
  let defaultNumber   v = ScalarNumber  (defaultPath, v)
  let defaultString   v = ScalarString  (defaultPath, v)

  let path = rootObject.Query

  check_scalar (defaultNull           ) (!!path?Object?Null         )
  check_scalar (defaultBoolean false  ) (!!path?Object?Boolean      )
  check_scalar (defaultNumber  123.   ) (!!path?Object?Number       )
  check_scalar (defaultString  "There") (!!path?Object?String       )

  check_scalar (defaultNull           ) (!!path?Object?Array    .[0])
  check_scalar (defaultBoolean true   ) (!!path?Object?Array    .[1])
  check_scalar (defaultNumber  0.     ) (!!path?Object?Array    .[2])
  check_scalar (defaultString  "Hello") (!!path?Object?Array    .[3])

  check_scalar (defaultNull           ) (!!path?Array .[0]          )
  check_scalar (defaultBoolean true   ) (!!path?Array .[1]          )
  check_scalar (defaultNumber  0.     ) (!!path?Array .[2]          )
  check_scalar (defaultString  "Hello") (!!path?Array .[3]          )

  check_eq false    path                .HasValue "HasValue: path"
  check_eq false    path?Object?Null    .HasValue "HasValue: path?Object?Null"
  check_eq true     path?Object?Boolean .HasValue "HasValue: path?Object?Boolean"
  check_eq true     path?Object?Number  .HasValue "HasValue: path?Object?Number"
  check_eq true     path?Object?String  .HasValue "HasValue: path?Object?String"
  check_eq false    path?Object?Invalid .HasValue "HasValue: path?Object?Invalid"
  check_eq false    path?Missing?Invalid.HasValue "HasValue: path?Missing?Invalid"

  check_eq false    path                .AsBool   "AsBool: path"
  check_eq false    path?Object?Null    .AsBool   "AsBool: path?Object?Null"
  check_eq false    path?Object?Boolean .AsBool   "AsBool: path?Object?Boolean"
  check_eq true     path?Object?Number  .AsBool   "AsBool: path?Object?Number"
  check_eq true     path?Object?String  .AsBool   "AsBool: path?Object?String"
  check_eq false    path?Object?Invalid .AsBool   "AsBool: path?Object?Invalid"
  check_eq false    path?Missing?Invalid.AsBool   "AsBool: path?Missing?Invalid"

  check_eq 0.       path                .AsFloat  "AsFloat: path"
  check_eq 0.       path?Object?Null    .AsFloat  "AsFloat: path?Object?Null"
  check_eq 0.       path?Object?Boolean .AsFloat  "AsFloat: path?Object?Boolean"
  check_eq 123.     path?Object?Number  .AsFloat  "AsFloat: path?Object?Number"
  check_eq 0.       path?Object?String  .AsFloat  "AsFloat: path?Object?String"
  check_eq 0.       path?Object?Invalid .AsFloat  "AsFloat: path?Object?Invalid"
  check_eq 0.       path?Missing?Invalid.AsFloat  "AsFloat: path?Missing?Invalid"

  check_eq ""       path                .AsString "AsString: path"
  check_eq ""       path?Object?Null    .AsString "AsString: path?Object?Null"
  check_eq "false"  path?Object?Boolean .AsString "AsString: path?Object?Boolean"
  check_eq "123"    path?Object?Number  .AsString "AsString: path?Object?Number"
  check_eq "There"  path?Object?String  .AsString "AsString: path?Object?String"
  check_eq ""       path?Object?Invalid .AsString "AsString: path?Object?Invalid"
  check_eq ""       path?Missing?Invalid.AsString "AsString: path?Missing?Invalid"

  let eRoot         = "NotScalar: root"
  let eInvalid      = "InvalidPath: root.Object!Invalid"
  let eMissing      = "InvalidPath: root!Missing!Invalid"

  check_eq eRoot    path                  .AsExpandedString     "AsExpandedString: path"
  check_eq "null"   path?Object?Null      .AsExpandedString     "AsExpandedString: path?Object?Null"
  check_eq "false"  path?Object?Boolean   .AsExpandedString     "AsExpandedString: path?Object?Boolean"
  check_eq "123"    path?Object?Number    .AsExpandedString     "AsExpandedString: path?Object?Number"
  check_eq "There"  path?Object?String    .AsExpandedString     "AsExpandedString: path?Object?String"
  check_eq eInvalid path?Object?Invalid   .AsExpandedString     "AsExpandedString: path?Object?Invalid"
  check_eq eMissing path?Missing?Invalid  .AsExpandedString     "AsExpandedString: path?Missing?Invalid"

  check_eq -1.      (path                 .ConvertToFloat -1.)  "ConvertToFloat: path"
  check_eq 0.       (path?Object?Null     .ConvertToFloat -1.)  "ConvertToFloat: path?Object?Null"
  check_eq 0.       (path?Object?Boolean  .ConvertToFloat -1.)  "ConvertToFloat: path?Object?Boolean"
  check_eq 123.     (path?Object?Number   .ConvertToFloat -1.)  "ConvertToFloat: path?Object?Number"
  check_eq -1.      (path?Object?String   .ConvertToFloat -1.)  "ConvertToFloat: path?Object?String"
  check_eq -1.      (path?Object?Invalid  .ConvertToFloat -1.)  "ConvertToFloat: path?Object?Invalid"
  check_eq -1.      (path?Missing?Invalid .ConvertToFloat -1.)  "ConvertToFloat: path?Missing?Invalid"


  let eLongPath = "InvalidPath: root.Object.Array.[0]!Invalid![100]!Missing"
  let aLongPath = path?Object?Array.[0]?Invalid.[100]?Missing.AsExpandedString

  check_eq eLongPath aLongPath "Long path"

// ----------------------------------------------------------------------------------------------

// ----------------------------------------------------------------------------------------------
[<EntryPoint>]
let main argv =
  try
    highlight "Starting JSON testcases..."

    Environment.CurrentDirectory <- AppDomain.CurrentDomain.BaseDirectory

#if !DUMP_JSON
    let dumper _            = ()
#else
    use dump = File.CreateText "dump.txt"
    let dumper (s : string) = dump.WriteLine s
#endif

    functionalTestCases             dumper
    functionalJsonNetTestCases      dumper
    functionalFSharpDataTestCases   dumper
    toStringTestCases               dumper
    errorReportingTestCases         dumper
    scalarToStringTestCases         dumper
    pathTestCases                   dumper
#if !DEBUG
    performanceTestCases            dumper
#endif

  with
  | ex -> errorf "EXCEPTION: %s" ex.Message

  if errors = 0 then
    success "No errors detected"
    0
  else
    errorf "Detected %d error(s)" errors
    9999
// ----------------------------------------------------------------------------------------------
