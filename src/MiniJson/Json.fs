﻿// ----------------------------------------------------------------------------------------------
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

/// MiniJson wants to be a minimal compliant JSON parser with reasonable performance 
///   JSON Specification: http://json.org/
///   JSON Lint         : http://jsonlint.com/
#if INTERNALIZE_MINIJSON
module internal MiniJson.JsonModule
#else
module MiniJson.JsonModule
#endif
open System
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.Text

type Json =
  | JsonNull
  | JsonBoolean of bool
  | JsonNumber  of float
  | JsonString  of string
  | JsonArray   of Json []
  | JsonObject  of (string*Json) []

let toString (doIndent : bool) (json : Json) : string =
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
    | JsonNull          -> str "null"
    | JsonBoolean true  -> str "true"
    | JsonBoolean false -> str "false"
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

  impl json

  sb.ToString ()

type ParseVisitor =
  interface
    abstract NullValue    : unit        -> bool
    abstract BoolValue    : bool        -> bool
    abstract NumberValue  : double      -> bool
    abstract StringValue  : string      -> bool
    abstract ArrayBegin   : unit        -> bool
    abstract ArrayEnd     : unit        -> bool
    abstract ObjectBegin  : unit        -> bool
    abstract ObjectEnd    : unit        -> bool
    abstract MemberKey    : string      -> bool
    abstract ExpectedChar : int*char    -> unit
    abstract Expected     : int*string  -> unit
    abstract Unexpected   : int*string  -> unit
  end

module Details =

  [<Literal>]
  let token_Null  = "null"

  [<Literal>]
  let token_True  = "true"

  [<Literal>]
  let token_False = "false"

  [<Literal>]
  let error_Prelude = "Failed to parse input as JSON"

  let inline neos (s : string) (pos : int) : bool = pos < s.Length
  let inline eos  (s : string) (pos : int) : bool = pos >= s.Length
  let inline ch   (s : string) (pos : int) : char = s.[pos]
  let inline adv  (p : byref<int>)                = p <- p + 1

  let inline raiseEos (v : ParseVisitor) (pos : int) : bool =
    v.Unexpected (pos, "EOS")
    false

  let inline pow n = pown 10.0 n

  let raiseValue (v : ParseVisitor) (pos : int) : bool =
    v.Expected      (pos, "null"  )
    v.Expected      (pos, "true"  )
    v.Expected      (pos, "false" )
    v.Expected      (pos, "STRING")
    v.ExpectedChar  (pos, '{'     )
    v.ExpectedChar  (pos, '['     )
    v.ExpectedChar  (pos, '-'     )
    v.Expected      (pos, "digit" )
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
    match c with
    | '0'
    | '1'
    | '2'
    | '3'
    | '4'
    | '5'
    | '6'
    | '7'
    | '8'
    | '9' -> true
    | _   -> false

  let inline isDigit19 (c : char) : bool =
    match c with
    | '1'
    | '2'
    | '3'
    | '4'
    | '5'
    | '6'
    | '7'
    | '8'
    | '9' -> true
    | _   -> false

  let inline test_Char (c : char) (s : string) (pos : int) : bool =
    neos s pos
    && ch s pos = c

  let inline tryConsume_Char (c : char) (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    if eos s pos then raiseEos v pos
    elif (ch s pos) = c then
      adv &pos
      true
    else
      v.ExpectedChar (pos, c)
      false

  let inline tryParse_AnyOf (cs : char []) (v : ParseVisitor) (s : string) (pos : byref<int>) (r : byref<char>): bool =
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
    if pos + tkl <= s.Length then
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
    else
      false

  let tryParse_Null (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    if tryConsume_Token token_Null s &pos then
      v.NullValue ()
    else
      raiseValue v pos

  let tryParse_True (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    if tryConsume_Token token_True s &pos then
      v.BoolValue true
    else
      raiseValue v pos

  let tryParse_False (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    if tryConsume_Token token_False s &pos then
      v.BoolValue false
    else
      raiseValue v pos

  let rec tryParse_UInt (first : bool) (v : ParseVisitor) (s : string) (pos : byref<int>) (r : byref<uint64>) : bool =
    if eos s pos then ignore <| raiseEos v pos; not first
    else
      let c = ch s pos
      if c >= '0' && c <= '9' then
        adv &pos
        r <- 10UL*r + (uint64 c - uint64 '0')
        tryParse_UInt false v s &pos &r
      else
        v.Expected (pos, "digit")
        not first

  let tryParse_UInt0 (v : ParseVisitor) (s : string) (pos : byref<int>) (r : byref<uint64>) : bool =
    // tryParse_UInt0 only consumes 0 if input is 0123, this in order to be conformant with spec
    let zero          = tryConsume_Char '0' v s &pos

    if zero then
      r <- 0UL
      true
    else
      tryParse_UInt true v s &pos &r

  let tryParse_Fraction (v : ParseVisitor) (s : string) (pos : byref<int>) (r : byref<float>) : bool =
    if tryConsume_Char '.' v s &pos then
      let spos        = pos
      let mutable ui  = 0UL
      if tryParse_UInt true v s &pos &ui then
        r <- (float ui) * (pow (spos - pos))
        true
      else
        false
    else
      true  // Fraction is optional

  let tryParse_Exponent (v : ParseVisitor) (s : string) (pos : byref<int>) (r : byref<int>) : bool =
    let mutable exp = ' '
    if tryParse_AnyOf [|'e';'E'|] v s &pos &exp then
      let mutable sign = '+'
      // Ignore as sign is optional
      ignore <| tryParse_AnyOf [|'+';'-'|] v s &pos &sign
      let mutable i = 0UL
      if tryParse_UInt true v s &pos &i then
        let inline sign v = if sign = '-' then -v else v
        r <- sign (int i)
        true
      else
        false
    else
      true  // Fraction is optional

  let tryParse_Number (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    let hasSign       = tryConsume_Char '-' v s &pos
    let inline sign v = if hasSign then -v else v

    let mutable i = 0UL
    let mutable f = 0.0
    let mutable e = 0

    let result =
      tryParse_UInt0 v s &pos &i
      && tryParse_Fraction v s &pos &f
      && tryParse_Exponent v s &pos &e

    if result then
      v.NumberValue (sign ((float i + f) * (pow e)))
    else
      false

  let rec tryParse_UnicodeChar (sb : StringBuilder) (v : ParseVisitor) (s : string) (pos : byref<int>) (n : int) (r : int) : bool =
    // TODO: Check this is tail recursive
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
        v.Expected (pos, "hexdigit")
        false

  let rec tryParse_Chars (sb : StringBuilder) (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    let inline app (c : char) = ignore <| sb.Append c

    if eos s pos then raiseEos v pos
    else
      let c = ch s pos
      match c with
      | '"'         -> true
      | '\r' | '\n' -> v.Unexpected (pos, "NEWLINE"); false
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
              tryParse_UnicodeChar sb v s &pos 4 0 // TODO: Check this is tail recursive
            | _ ->
              v.ExpectedChar (pos, '"' )
              v.ExpectedChar (pos, '\\')
              v.ExpectedChar (pos, '/' )
              v.ExpectedChar (pos, 'b' )
              v.ExpectedChar (pos, 'f' )
              v.ExpectedChar (pos, 'n' )
              v.ExpectedChar (pos, 'r' )
              v.ExpectedChar (pos, 't' )
              v.ExpectedChar (pos, 'u' )
              false
          result && tryParse_Chars sb v s &pos // TODO: Check this is tail recursive
      | _           ->
        adv &pos
        app c
        tryParse_Chars sb v s &pos // TODO: Check this is tail recursive

  let tryParse_ToStringBuilder (sb : StringBuilder) (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    ignore <| sb.Clear ()
    tryConsume_Char           '"' v s &pos
    && tryParse_Chars      sb     v s &pos
    && tryConsume_Char        '"' v s &pos

  let tryParse_String (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    // TODO: Reuse StringBuilder
    let sb = StringBuilder ()
    tryParse_ToStringBuilder sb v s &pos
    && v.StringValue (sb.ToString ())

  let tryParse_MemberKey (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    // TODO: Reuse StringBuilder
    let sb = StringBuilder ()
    tryParse_ToStringBuilder sb v s &pos
    && v.MemberKey (sb.ToString ())

  let inline tryConsume_Delimiter first (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    if first then true
    else
      tryConsume_Char         ',' v s &pos
      && consume_WhiteSpace         s &pos

  let rec tryParse_ArrayValues first (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    if test_Char ']' s pos then
      true
    else
      tryConsume_Delimiter    first v s &pos
      && tryParse_Value             v s &pos
      && tryParse_ArrayValues false v s &pos      // TODO: Check this is tail recursive

  and tryParse_Array (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    tryConsume_Char           '[' v s &pos
    && consume_WhiteSpace           s &pos
    && v.ArrayBegin ()
    && tryParse_ArrayValues true  v s &pos
    && tryConsume_Char        ']' v s &pos
    && v.ArrayEnd ()

  and tryParse_ObjectMembers first (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    if test_Char '}' s pos then
      true
    else
      tryConsume_Delimiter      first v s &pos
      && tryParse_MemberKey           v s &pos
      && consume_WhiteSpace             s &pos
      && tryConsume_Char          ':' v s &pos
      && consume_WhiteSpace             s &pos
      && tryParse_Value               v s &pos
      && tryParse_ObjectMembers false v s &pos      // TODO: Check this is tail recursive

  and tryParse_Object (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    tryConsume_Char               '{' v s &pos
    && consume_WhiteSpace               s &pos
    && v.ObjectBegin ()
    && tryParse_ObjectMembers    true v s &pos
    && tryConsume_Char            '}' v s &pos
    && v.ObjectEnd ()

  and tryParse_Value (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    if eos s pos then raiseEos v pos
    else
      let result =
        match ch s pos with
        | 'n'                 -> tryParse_Null    v s &pos
        | 't'                 -> tryParse_True    v s &pos
        | 'f'                 -> tryParse_False   v s &pos
        | '['                 -> tryParse_Array   v s &pos
        | '{'                 -> tryParse_Object  v s &pos
        | '"'                 -> tryParse_String  v s &pos
        | '-'                 -> tryParse_Number  v s &pos
        | c when isDigit c    -> tryParse_Number  v s &pos
        | _                   -> raiseValue v pos
      result && consume_WhiteSpace s &pos

  let tryParse_Eos (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    if neos s pos then v.Expected (pos, "EOS"); false
    else
      true

  let tryParse (v : ParseVisitor) (s : string) (pos : byref<int>) : bool =
    consume_WhiteSpace s &pos
    && (tryParse_Object v s &pos) || (tryParse_Array v s &pos)
    && consume_WhiteSpace s &pos
    && tryParse_Eos v s &pos

  type JsonBuilder =
    | BuilderRoot    of Json ref
    | BuilderObject  of string ref*ResizeArray<string*Json>
    | BuilderArray   of ResizeArray<Json>

  [<Sealed>]
  type JsonParseVisitor() =
    let context       = Stack<JsonBuilder> ()

    let push v        =
      context.Push v
      true

    let pop ()        =
      match context.Pop () with
      | BuilderRoot   vr      -> !vr
      | BuilderObject (_,ms)  -> JsonObject (ms.ToArray ())
      | BuilderArray  vs      -> JsonArray (vs.ToArray ())

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

    let defaultSize = 4

    interface ParseVisitor with
      override x.NullValue    ()      = add <| JsonNull
      override x.BoolValue    v       = add <| JsonBoolean v
      override x.NumberValue  v       = add <| JsonNumber v
      override x.StringValue  v       = add <| JsonString v

      override x.ArrayBegin   ()      = push (BuilderArray <| ResizeArray<_>(defaultSize))
      override x.ArrayEnd     ()      = add <| pop ()
      override x.ObjectBegin  ()      = push (BuilderObject <| (ref "", ResizeArray<_>(defaultSize)))
      override x.ObjectEnd    ()      = add <| pop ()
      override x.MemberKey    v       = setKey v

      override x.ExpectedChar (p,e)   = ()
      override x.Expected     (p,e)   = ()
      override x.Unexpected   (p,ue)  = ()

    member x.Root ()  =
      Debug.Assert (context.Count = 1)
      pop ()

  [<Sealed>]
  type JsonErrorParseVisitor(epos : int) =
    let expectedChars = ResizeArray<char> ()
    let expected      = ResizeArray<string> ()
    let unexpected    = ResizeArray<string> ()

    let filter f      = f |> Seq.sort |> Seq.distinct |> Seq.toArray

    interface ParseVisitor with
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

type ParseResult =
  | Success of Json
  | Failure of string*int

let parse (fullErrorInfo : bool) (s : string) : ParseResult =
  let mutable pos = 0
  let v           = Details.JsonParseVisitor ()

  match Details.tryParse (upcast v) s &pos, fullErrorInfo with
  | true  , _     ->
    Success (v.Root ())
  | false , false ->
    Failure (Details.error_Prelude, pos)
  | false , true  ->
    let mutable epos  = 0
    let ev            = Details.JsonErrorParseVisitor (pos)

    ignore <| Details.tryParse (upcast ev) s &epos

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
      if s.Length < windowSize then
        0, s.Length - 1, pos
      else
        let hs  = windowSize / 2
        let b   = pos - hs
        let e   = pos + hs
        let ab  = max 0 b
        let ae  = min (s.Length - 1) (e + ab - b)
        let ap  = pos - ab
        ab, ae, ap

    strl Details.error_Prelude
    for i = windowBegin to windowEnd do
      let c =
        match s.[i] with
        | 'n'
        | 'r' -> ' '
        | c   -> c
      ch c
    line ()
    ignore <| sb.Append ('-', windowPos)
    str "^ Pos: "
    ignore <| sb.Append pos
    values "Expected: " e
    values "Unexpected: " ue

    Failure (sb.ToString (), pos)
