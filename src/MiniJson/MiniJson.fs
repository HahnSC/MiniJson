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

/// MiniJson aims to be a minimal yet conforming JSON parser with reasonable performance and decent error reporting
///   JSON Specification: http://json.org/
///   JSON Lint         : http://jsonlint.com/
#if PUBLIC_MINIJSON
module MiniJson.JsonModule
#else
// Due to what seems to be an issue with the F# compiler preventing
//  access to internal operator ? from within the same assembly
//  define INTERNAL_MINIJSON_WORKAROUND to suppress internalizing of
//  MiniJson.
#if INTERNAL_MINIJSON_WORKAROUND
module Internal.MiniJson.JsonModule
#else
module internal Internal.MiniJson.JsonModule
#endif
#endif
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.Text

module internal Tokens =
  [<Literal>]
  let Null      = "null"

  [<Literal>]
  let True      = "true"

  [<Literal>]
  let False     = "false"

  [<Literal>]
  let Digit     = "digit"

  [<Literal>]
  let HexDigit  = "hexdigit"

  [<Literal>]
  let EOS       = "EOS"

  [<Literal>]
  let NewLine   = "NEWLINE"

/// Represents a JSON document
type Json =
  // ()         - Represents a JSON null value
  | JsonNull
  // (value)    - Represents a JSON boolean value
  | JsonBoolean of bool
  // (value)    - Represents a JSON number value
  | JsonNumber  of float
  // (value)    - Represents a JSON string value
  | JsonString  of string
  // (values)   - Represents a JSON array value
  | JsonArray   of Json []
  // (members)  - Represents a JSON object value
  | JsonObject  of (string*Json) []

  /// Converts a JSON document into a string
  ///   doIndent  : True to indent
  member x.ToString (doIndent : bool) : string =
    let sb = StringBuilder ()

    let newline, indent, inc, dec =
      let doNothing () = ()
      if doIndent then
        let current = ref 0

        let newline ()  = ignore <| sb.AppendLine ()
        let indent ()   = ignore <| sb.Append (' ', !current)
        let inc ()      = current := !current + 2
        let dec ()      = current := !current - 2

        newline, indent, inc, dec
      else
        doNothing, doNothing, doNothing, doNothing

    let inline str (s : string)     = ignore <| sb.Append s
    let inline ch  (c : char)       = ignore <| sb.Append c
    let inline num (f : float)      = ignore <| sb.AppendFormat (CultureInfo.InvariantCulture, "{0}", f)

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
      newline ()
      inc ()
      let ee = vs.Length - 1
      for i = 0 to ee do
        let v = vs.[i]
        indent ()
        a v
        if i < ee then
          ch ','
        newline ()
      dec ()
      indent ()
      ch e

    let rec impl j =
      match j with
      | JsonNull          -> str Tokens.Null
      | JsonBoolean true  -> str Tokens.True
      | JsonBoolean false -> str Tokens.False
      | JsonNumber n      -> num n
      | JsonString s      -> estr s
      | JsonArray vs      -> values '[' ']' vs impl
      | JsonObject ms     -> values '{' '}' ms implkv
    and implkv (k,v) =
      estr k
      ch ':'
      newline ()
      inc ()
      indent ()
      impl v
      dec ()

    let json =
      match x with
      | JsonNull
      | JsonBoolean _
      | JsonNumber  _
      | JsonString  _ -> JsonArray [|x|]  // In order to be valid JSON
      | JsonArray   _
      | JsonObject  _ -> x

    impl json

    sb.ToString ()

  /// Converts a JSON document into a string
  override x.ToString () : string =
    x.ToString false

/// Converts a JSON document into a string
///   doIndent  : True to indent
///   json      : The JSON document
let toString doIndent (json : Json) : string =
  json.ToString doIndent

/// IParseVisitor is implemented by users wanting to parse
///   a JSON document into a data structure other than MiniJson.Json
type IParseVisitor =
  interface
    abstract NullValue    : unit          -> bool
    abstract BoolValue    : bool          -> bool
    abstract NumberValue  : double        -> bool
    abstract StringValue  : StringBuilder -> bool
    abstract ArrayBegin   : unit          -> bool
    abstract ArrayEnd     : unit          -> bool
    abstract ObjectBegin  : unit          -> bool
    abstract ObjectEnd    : unit          -> bool
    abstract MemberKey    : StringBuilder -> bool
    abstract ExpectedChar : int*char      -> unit
    abstract Expected     : int*string    -> unit
    abstract Unexpected   : int*string    -> unit
  end

module internal Details =
  [<Literal>]
  let DefaultSize = 16

  [<Literal>]
  let ErrorPrelude = "Failed to parse input as JSON"

  let inline neos (s : string) (pos : int) : bool = pos < s.Length
  let inline eos  (s : string) (pos : int) : bool = pos >= s.Length
  let inline ch   (s : string) (pos : int) : char = s.[pos]
  let inline adv  (p : byref<int>)                = p <- p + 1

  let inline raiseEos (v : IParseVisitor) (pos : int) : bool =
    v.Unexpected (pos, Tokens.EOS)
    false

  let inline pow n = pown 10.0 n

  let expectedChars (v : IParseVisitor) (p : int) (chars : string) : unit =
    let e = chars.Length - 1
    for i = 0 to e do
      v.ExpectedChar (p, chars.[i])

  let raiseValue (v : IParseVisitor) (pos : int) : bool =
    v.Expected      (pos, Tokens.Null )
    v.Expected      (pos, Tokens.True )
    v.Expected      (pos, Tokens.False)
    v.Expected      (pos, Tokens.Digit)
    expectedChars v pos "\"{[-"
    false

  let raiseRoot (v : IParseVisitor) (pos : int) : bool =
    expectedChars v pos "{["
    false

  let inline isWhiteSpace (c : char) : bool =
    match c with
    | '\t'
    | '\n'
    | '\r'
    | ' ' -> true
    | _   -> false

  let inline consume_WhiteSpace (s : string) (pos : byref<int>) : bool =
    let l = s.Length
    while pos < l && (isWhiteSpace s.[pos]) do
      adv &pos
    true

  let inline isDigit (c : char) : bool =
    c >= '0' && c <= '9'

  let inline test_Char (c : char) (s : string) (pos : int) : bool =
    neos s pos
    && ch s pos = c

  let inline tryConsume_Char (c : char) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if eos s pos then raiseEos v pos
    elif (ch s pos) = c then
      adv &pos
      true
    else
      v.ExpectedChar (pos, c)
      false

  let inline tryParse_AnyOf (cs : char []) (v : IParseVisitor) (s : string) (pos : byref<int>) (r : byref<char>): bool =
    if eos s pos then raiseEos v pos
    else
      let c = ch s pos
      if cs |> Array.contains c then
        r <- c
        adv &pos
        true
      else
        for c in cs do
          v.ExpectedChar (pos, c)
        false

  let inline tryConsume_Token (tk : string) (s : string) (pos : byref<int>) : bool =
    let tkl = tk.Length
    let spos = pos
    let mutable tpos = 0

    while tpos < tkl && tk.[tpos] = s.[pos] do
      adv &tpos
      adv &pos

    if tpos = tkl then true
    else
      // To support error reporting, move back on failure
      pos <- spos
      false

  let tryParse_Null (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if tryConsume_Token Tokens.Null s &pos then
      v.NullValue ()
    else
      raiseValue v pos

  let tryParse_True (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if tryConsume_Token Tokens.True s &pos then
      v.BoolValue true
    else
      raiseValue v pos

  let tryParse_False (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if tryConsume_Token Tokens.False s &pos then
      v.BoolValue false
    else
      raiseValue v pos

  let rec tryParse_UInt (first : bool) (v : IParseVisitor) (s : string) (pos : byref<int>) (r : byref<float>) : bool =
    let z = float '0'
    if eos s pos then ignore <| raiseEos v pos; not first
    else
      let c = ch s pos
      if c >= '0' && c <= '9' then
        adv &pos
        r <- 10.0*r + (float c - z)
        tryParse_UInt false v s &pos &r
      else
        v.Expected (pos, Tokens.Digit)
        not first

  let tryParse_UInt0 (v : IParseVisitor) (s : string) (pos : byref<int>) (r : byref<float>) : bool =
    // tryParse_UInt0 only consumes 0 if input is 0123, this in order to be conformant with spec
    let zero          = tryConsume_Char '0' v s &pos

    if zero then
      r <- 0.0
      true
    else
      tryParse_UInt true v s &pos &r

  let tryParse_Fraction (v : IParseVisitor) (s : string) (pos : byref<int>) (r : byref<float>) : bool =
    if tryConsume_Char '.' v s &pos then
      let spos        = pos
      let mutable uf  = 0.
      if tryParse_UInt true v s &pos &uf then
        r <- (float uf) * (pow (spos - pos))
        true
      else
        false
    else
      true  // Fraction is optional

  let tryParse_Exponent (v : IParseVisitor) (s : string) (pos : byref<int>) (r : byref<int>) : bool =
    let mutable exp = ' '
    if tryParse_AnyOf [|'e';'E'|] v s &pos &exp then
      let mutable sign = '+'
      // Ignore as sign is optional
      ignore <| tryParse_AnyOf [|'+';'-'|] v s &pos &sign
      // TODO: Parsing exponent as float seems unnecessary
      // TODO: Check out of range for exponent
      let mutable uf = 0.0
      if tryParse_UInt true v s &pos &uf then
        let inline sign v = if sign = '-' then -v else v
        r <- sign (int uf)
        true
      else
        false
    else
      true  // Fraction is optional

  let tryParse_Number (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    let hasSign       = tryConsume_Char '-' v s &pos
    let inline sign v = if hasSign then -v else v

    let mutable i = 0.0
    let mutable f = 0.0
    let mutable e = 0

    let result =
      tryParse_UInt0 v s &pos &i
      && tryParse_Fraction v s &pos &f
      && tryParse_Exponent v s &pos &e

    if result then
      v.NumberValue (sign ((i + f) * (pow e)))
    else
      false

  let rec tryParse_UnicodeChar (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) (n : int) (r : int) : bool =
    if n = 0 then
      ignore <| sb.Append (char r)
      true
    elif eos s pos then raiseEos v pos
    else
      let sr  = r <<< 4
      let   c = ch s pos
      if    c >= '0' && c <= '9'  then adv &pos ; tryParse_UnicodeChar sb v s &pos (n - 1) (sr + (int c - int '0'))
      elif  c >= 'A' && c <= 'F'  then adv &pos ; tryParse_UnicodeChar sb v s &pos (n - 1) (sr + (int c - int 'A' + 10))
      elif  c >= 'a' && c <= 'f'  then adv &pos ; tryParse_UnicodeChar sb v s &pos (n - 1) (sr + (int c - int 'a' + 10))
      else
        v.Expected (pos, Tokens.HexDigit)
        false

  let rec tryParse_Chars (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    let inline app (c : char) = ignore <| sb.Append c

    if eos s pos then raiseEos v pos
    else
      let c = ch s pos
      match c with
      | '"'         -> true
      | '\r' | '\n' -> v.Unexpected (pos, Tokens.NewLine); false
      | '\\'        ->
        adv &pos
        if eos s pos then raiseEos v pos
        else
          let e = ch s pos
          let result =
            match e with
            | '"'
            | '\\'
            | '/' -> app e    ; adv &pos; true
            | 'b' -> app '\b' ; adv &pos; true
            | 'f' -> app '\f' ; adv &pos; true
            | 'n' -> app '\n' ; adv &pos; true
            | 'r' -> app '\r' ; adv &pos; true
            | 't' -> app '\t' ; adv &pos; true
            | 'u' ->
              adv &pos
              tryParse_UnicodeChar sb v s &pos 4 0
            | _ ->
              expectedChars v pos "\"\\/bfnrtu"
              false
          result && tryParse_Chars sb v s &pos
      | _           ->
        adv &pos
        app c
        tryParse_Chars sb v s &pos

  let tryParse_ToStringBuilder (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    ignore <| sb.Clear ()
    tryConsume_Char           '"' v s &pos
    && tryParse_Chars      sb     v s &pos
    && tryConsume_Char        '"' v s &pos

  let tryParse_String (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    tryParse_ToStringBuilder sb v s &pos
    && v.StringValue sb

  let tryParse_MemberKey (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    tryParse_ToStringBuilder sb v s &pos
    && v.MemberKey sb

  let inline tryConsume_Delimiter first (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if first then true
    else
      tryConsume_Char         ',' v s &pos
      && consume_WhiteSpace         s &pos

  let rec tryParse_ArrayValues first (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if test_Char ']' s pos then
      true
    else
      tryConsume_Delimiter    first     v s &pos
      && tryParse_Value             sb  v s &pos
      && tryParse_ArrayValues false sb  v s &pos

  and tryParse_Array (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    tryConsume_Char           '['     v s &pos
    && consume_WhiteSpace               s &pos
    && v.ArrayBegin ()
    && tryParse_ArrayValues true  sb  v s &pos
    && tryConsume_Char        ']'     v s &pos
    && v.ArrayEnd ()

  and tryParse_ObjectMembers first (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if test_Char '}' s pos then
      true
    else
      tryConsume_Delimiter      first     v s &pos
      && tryParse_MemberKey           sb  v s &pos
      && consume_WhiteSpace                 s &pos
      && tryConsume_Char          ':'     v s &pos
      && consume_WhiteSpace                 s &pos
      && tryParse_Value               sb  v s &pos
      && tryParse_ObjectMembers false sb  v s &pos

  and tryParse_Object (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    tryConsume_Char               '{'     v s &pos
    && consume_WhiteSpace                   s &pos
    && v.ObjectBegin ()
    && tryParse_ObjectMembers    true sb  v s &pos
    && tryConsume_Char            '}'     v s &pos
    && v.ObjectEnd ()

  and tryParse_Value (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if eos s pos then raiseEos v pos
    else
      let result =
        match ch s pos with
        | 'n'                 -> tryParse_Null        v s &pos
        | 't'                 -> tryParse_True        v s &pos
        | 'f'                 -> tryParse_False       v s &pos
        | '['                 -> tryParse_Array   sb  v s &pos
        | '{'                 -> tryParse_Object  sb  v s &pos
        | '"'                 -> tryParse_String  sb  v s &pos
        | '-'                 -> tryParse_Number      v s &pos
        | c when isDigit c    -> tryParse_Number      v s &pos
        | _                   -> raiseValue v pos
      result && consume_WhiteSpace s &pos
  let tryParse_RootValue (sb : StringBuilder) (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if eos s pos then raiseEos v pos
    else
      let result =
        match ch s pos with
        | '['                 -> tryParse_Array  sb v s &pos
        | '{'                 -> tryParse_Object sb v s &pos
        | _                   -> raiseRoot v pos
      result && consume_WhiteSpace s &pos

  let tryParse_Eos (v : IParseVisitor) (s : string) (pos : byref<int>) : bool =
    if neos s pos then v.Expected (pos, Tokens.EOS); false
    else
      true

  [<NoEquality>]
  [<NoComparison>]
  type JsonBuilder =
    | BuilderRoot    of Json ref
    | BuilderObject  of string ref*ResizeArray<string*Json>
    | BuilderArray   of ResizeArray<Json>

  [<Sealed>]
  [<NoEquality>]
  [<NoComparison>]
  type JsonParseVisitor() =
    let context             = Stack<JsonBuilder> (DefaultSize)
    let freeObjectBuilders  = Stack<JsonBuilder> (DefaultSize)
    let freeArrayBuilders   = Stack<JsonBuilder> (DefaultSize)

    let pushObject ()        =
      if freeObjectBuilders.Count > 0 then
        context.Push (freeObjectBuilders.Pop ())
      else
        context.Push (BuilderObject <| (ref System.String.Empty, ResizeArray<_>(DefaultSize)))
      true

    let pushArray ()        =
      if freeArrayBuilders.Count > 0 then
        context.Push (freeArrayBuilders.Pop ())
      else
        context.Push (BuilderArray <| ResizeArray<_>(DefaultSize))
      true

    let pop ()        =
      let v = context.Pop ()
      match v with
      | BuilderRoot   vr      -> !vr
      | BuilderObject (rk,ms)  ->
        let result  = JsonObject (ms.ToArray ())
        rk := System.String.Empty
        ms.Clear ()
        freeObjectBuilders.Push v
        result
      | BuilderArray  vs      ->
        let result  = JsonArray (vs.ToArray ())
        vs.Clear ()
        freeArrayBuilders.Push v
        result

    let setKey k      =
      match context.Peek () with
      | BuilderRoot   vr      -> ()
      | BuilderObject (rk,_)  -> rk := k
      | BuilderArray  vs      -> ()
      true

    let add v         =
      match context.Peek () with
      | BuilderRoot   vr      -> vr := v
      | BuilderObject (rk,ms) -> ms.Add (!rk, v)
      | BuilderArray  vs      -> vs.Add v
      true

    do
      context.Push <| (BuilderRoot <| ref JsonNull)

    interface IParseVisitor with
      override x.NullValue    ()      = add <| JsonNull
      override x.BoolValue    v       = add <| JsonBoolean v
      override x.NumberValue  v       = add <| JsonNumber v
      override x.StringValue  v       = add <| JsonString (v.ToString ())

      override x.ArrayBegin   ()      = pushArray ()
      override x.ArrayEnd     ()      = add <| pop ()
      override x.ObjectBegin  ()      = pushObject ()
      override x.ObjectEnd    ()      = add <| pop ()
      override x.MemberKey    v       = setKey <| v.ToString ()

      override x.ExpectedChar (p,e)   = ()
      override x.Expected     (p,e)   = ()
      override x.Unexpected   (p,ue)  = ()

    member x.Root ()  =
      Debug.Assert (context.Count = 1)
      pop ()

  [<Sealed>]
  [<NoEquality>]
  [<NoComparison>]
  type JsonErrorParseVisitor(epos : int) =
    let expectedChars = ResizeArray<char>   (DefaultSize)
    let expected      = ResizeArray<string> (DefaultSize)
    let unexpected    = ResizeArray<string> (DefaultSize)

    let filter f      = f |> Seq.sort |> Seq.distinct |> Seq.toArray

    interface IParseVisitor with
      override x.NullValue    ()      = true
      override x.BoolValue    v       = true
      override x.NumberValue  v       = true
      override x.StringValue  v       = true

      override x.ArrayBegin   ()      = true
      override x.ArrayEnd     ()      = true
      override x.ObjectBegin  ()      = true
      override x.ObjectEnd    ()      = true
      override x.MemberKey    v       = true

      override x.ExpectedChar (p,e)   = if p = epos then expectedChars.Add e
      override x.Expected     (p,e)   = if p = epos then expected.Add e
      override x.Unexpected   (p,ue)  = if p = epos then unexpected.Add ue

    member x.ExpectedChars  = filter expectedChars
    member x.Expected       = filter expected
    member x.Unexpected     = filter unexpected

open Details

/// Attempts to parse a JSON document from a string
///   visitor : Parser visitor object
///   input   : Input string
let tryParse (visitor : IParseVisitor) (input : string) (pos : byref<int>) : bool =
  let sb = StringBuilder DefaultSize
  consume_WhiteSpace                input &pos
  && tryParse_RootValue sb  visitor input &pos
  && tryParse_Eos           visitor input &pos

/// Returned by parse function
type ParseResult =
  /// (json) - Holds the parsed JSON document
  | Success of Json
  /// (message, pos) - Holds the error description and position of failure
  | Failure of string*int

/// Attempts to parse a JSON document from a string
///   fullErrorInfo : True to generate full errorinfo
///                   False only shows position (faster)
///   input         : Input string
let parse (fullErrorInfo : bool) (input : string) : ParseResult =
  let mutable pos = 0
  let v           = JsonParseVisitor ()

  match tryParse (upcast v) input &pos, fullErrorInfo with
  | true  , _     ->
    Success (v.Root ())
  | false , false ->
    Failure (ErrorPrelude, pos)
  | false , true  ->
    let mutable epos  = 0
    let ev            = JsonErrorParseVisitor (pos)

    ignore <| tryParse (upcast ev) input &epos

    let sb = StringBuilder ()
    let inline str  (s : string)  = ignore <| sb.Append s
    let inline strl (s : string)  = ignore <| sb.AppendLine s
    let inline ch   (c : char)    = ignore <| sb.Append c
    let inline line ()            = ignore <| sb.AppendLine ()

    let e =
      Seq.concat
        [|
          ev.ExpectedChars  |> Seq.map (fun c -> "'" + (c.ToString ()) + "'")
          upcast ev.Expected
        |]
      |> Seq.toArray
    let ue = ev.Unexpected

    let values prefix (vs : string []) =
      if vs.Length = 0 then ()
      else
        line ()
        str prefix
        let e = vs.Length - 1
        for i = 0 to e do
          let v = vs.[i]
          if i = 0 then ()
          elif i = e then str " or "
          else str ", "
          str v

    let windowSize = 60
    let windowBegin,windowEnd,windowPos =
      if input.Length < windowSize then
        0, input.Length - 1, pos
      else
        let hs  = windowSize / 2
        let b   = pos - hs
        let e   = pos + hs
        let ab  = max 0 b
        let ae  = min (input.Length - 1) (e + ab - b)
        let ap  = pos - ab
        ab, ae, ap

    strl ErrorPrelude
    for i = windowBegin to windowEnd do
      let c =
        match input.[i] with
        | '\n'
        | '\r'  -> ' '
        | c     -> c
      ch c
    line ()
    ignore <| sb.Append ('-', windowPos)
    str "^ Pos: "
    ignore <| sb.Append pos
    values "Expected: " e
    values "Unexpected: " ue

    Failure (sb.ToString (), pos)
