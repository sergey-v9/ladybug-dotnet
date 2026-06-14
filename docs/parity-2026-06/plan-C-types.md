# WS-C — Type Fidelity + Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Make DECIMAL reads lossless (`decimal` when representable, else `LadybugDecimal`, never a bare string), expose first-class fixed `ARRAY` and tagged `UNION` reads on `Value`, and add `decimal`/`LadybugDecimal`/`BigInteger`(INT128)/`byte[]`(BLOB)/struct-dictionary/map binding to `PreparedStatement`.

**Architecture:** A new value type `LadybugDecimal` (unscaled `BigInteger` + `byte Scale`) carries DECIMAL data with no precision loss; it is pure managed logic and fully unit-testable without the native engine. `Value.GetValue()` keeps materializing every Ladybug type but routes DECIMAL through `LadybugDecimal`, honors fixed-array length, and tags UNION members. `PreparedStatement` gains typed `Bind` overloads and extends its `Bind(object?)` dispatch and `CreateNativeValue` switch to use the value creators WS-A pinned (`Native.ValueCreateInt128/ValueCreateDecimal/ValueCreateInternalId/ValueCreateStruct/ValueCreateMap/ValueCreateNullWithDataType`).

**Tech Stack:** C# (dual TFM `net10.0;netstandard2.0`), `System.Numerics.BigInteger` (ns2.0-safe, in `System.Runtime.Numerics`), hand-written P/Invoke via `Native.*`, xUnit + `Xunit.SkippableFact` with the `TestEnvironment.NativeAvailable` gate.

---

## Files

**Create**
- `src/LadybugDB/LadybugDecimal.cs` — the lossless DECIMAL value type (pure managed).
- `test/LadybugDB.Tests/LadybugDecimalTests.cs` — non-gated unit tests for `LadybugDecimal`.
- `test/LadybugDB.Tests/BindingRoundTripTests.cs` — native-gated `SkippableFact` binding round-trips.

**Modify**
- `src/LadybugDB/Value.cs` — DECIMAL → `LadybugDecimal`/`decimal`; add `GetDecimal()`; first-class ARRAY (fixed length) + UNION (tagged) reads.
- `src/LadybugDB/PreparedStatement.cs` — new typed `Bind` overloads; extend `Bind(object?)` dispatch + `CreateNativeValue`.
- `src/LadybugDB/DataTypeId.cs` — (only if a helper enum/comment is needed; see Task 9 — likely untouched).
- `src/LadybugDB/GraphTypes.cs` — add the `Union` tagged-value record.
- `test/LadybugDB.Tests/TypeMappingTests.cs` — add DECIMAL/ARRAY/UNION read assertions (native-gated).

**Consumes (owned by WS-A — do NOT edit, only call)**
- `src/LadybugDB/Interop/Native.LibraryImport.cs` / `Native.DllImport.cs` — must already declare:
  `ValueCreateInt128(LbugInt128)`, `ValueCreateDecimal(byte[] valUtf8, uint precision, uint scale)`,
  `ValueCreateInternalId(LbugInternalId)`, `ValueCreateStruct(ulong, IntPtr[] fieldNames, IntPtr[] fieldValues, out IntPtr)`,
  `ValueCreateMap(ulong, IntPtr[] keys, IntPtr[] values, out IntPtr)`,
  `ValueCreateNullWithDataType(ref LbugLogicalType, ...)`, `Int128FromString`, `DataTypeGetNumElementsInArray`.
  **If a needed declaration is missing, STOP and flag WS-A — do not add interop here (WS-A owns those files).**

---

## Pre-flight (read once, do not skip)

- [ ] Confirm you are on the WS-C worktree/branch (`ws/c-types` off the post-Phase-1 integration commit) and that WS-A's interop is present: run
      `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug` and confirm it is green before changing anything.
- [ ] Verify the WS-A value creators exist. Search the interop:
      `rg -n "ValueCreateDecimal|ValueCreateInt128|ValueCreateStruct|ValueCreateMap|ValueCreateInternalId|ValueCreateNullWithDataType|DataTypeGetNumElementsInArray|Int128FromString" W:\code\ladybug\tools\csharp_api\src\LadybugDB\Interop`
      Expect hits in BOTH `Native.LibraryImport.cs` and `Native.DllImport.cs`. If any are missing, flag WS-A and pause WS-C.
- [ ] Note the exact native DECIMAL contracts from `src/include/c_api/lbug.h`:
      - Read: `lbug_value_get_decimal_as_string(value, char** out)` → already wrapped as `Native.ValueGetDecimalAsString` (returns the exact textual form, e.g. `"-12.3400"`).
      - Create: `lbug_value_create_decimal(const char* val_, uint32_t precision, uint32_t scale)` → `Native.ValueCreateDecimal`.
      - ARRAY element count: `lbug_data_type_get_num_elements_in_array(logical_type, uint64_t* out)` → `Native.DataTypeGetNumElementsInArray`.
      - INT128 from string: `lbug_int128_t_from_string(const char* str, lbug_int128_t* out)` → `Native.Int128FromString`.

---

## TASK 1 — `LadybugDecimal`: struct skeleton + `ToString()` (exact textual form)

- [ ] **Write the failing test.** Create `test/LadybugDB.Tests/LadybugDecimalTests.cs`:
  ```csharp
  using System.Numerics;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests;

  /// <summary>
  /// Unit tests for <see cref="LadybugDecimal"/>. These exercise pure managed arithmetic and
  /// formatting and do NOT require the native engine, so they are plain facts (never gated).
  /// </summary>
  public sealed class LadybugDecimalTests
  {
      [Fact]
      public void ToString_renders_unscaled_with_decimal_point()
      {
          var d = new LadybugDecimal(new BigInteger(12345), 2);
          Assert.Equal("123.45", d.ToString());
      }

      [Fact]
      public void ToString_renders_negative_with_leading_zero()
      {
          var d = new LadybugDecimal(new BigInteger(-5), 3);
          Assert.Equal("-0.005", d.ToString());
      }

      [Fact]
      public void ToString_with_zero_scale_has_no_point()
      {
          var d = new LadybugDecimal(new BigInteger(42), 0);
          Assert.Equal("42", d.ToString());
      }

      [Fact]
      public void Unscaled_and_scale_are_exposed()
      {
          var d = new LadybugDecimal(new BigInteger(12345), 2);
          Assert.Equal(new BigInteger(12345), d.Unscaled);
          Assert.Equal((byte)2, d.Scale);
      }
  }
  ```
- [ ] **Run / expect FAIL** (does not compile — `LadybugDecimal` does not exist yet):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter FullyQualifiedName~LadybugDecimalTests`
- [ ] **Implement minimally.** Create `src/LadybugDB/LadybugDecimal.cs`:
  ```csharp
  using System;
  using System.Globalization;
  using System.Numerics;

  namespace LadybugDB;

  /// <summary>
  /// A lossless fixed-point DECIMAL value: an unscaled <see cref="BigInteger"/> mantissa plus a
  /// <see cref="Scale"/> giving the number of fractional digits. The numeric value is
  /// <c>Unscaled * 10^-Scale</c>. Unlike <see cref="decimal"/> it has no range or precision limit,
  /// so a DECIMAL column is always materialized without loss. Use <see cref="ToDecimal"/> /
  /// <see cref="TryToDecimal"/> to project to the CLR <see cref="decimal"/> when it fits.
  /// </summary>
  public readonly struct LadybugDecimal : IEquatable<LadybugDecimal>
  {
      /// <summary>The unscaled integer mantissa (the value with the decimal point removed).</summary>
      public BigInteger Unscaled { get; }

      /// <summary>The number of fractional digits (the power of ten the mantissa is divided by).</summary>
      public byte Scale { get; }

      /// <summary>Creates a decimal from its unscaled mantissa and scale.</summary>
      public LadybugDecimal(BigInteger unscaled, byte scale)
      {
          Unscaled = unscaled;
          Scale = scale;
      }

      /// <inheritdoc />
      public override string ToString()
      {
          if (Scale == 0)
          {
              return Unscaled.ToString(CultureInfo.InvariantCulture);
          }

          bool negative = Unscaled.Sign < 0;
          string digits = BigInteger.Abs(Unscaled).ToString(CultureInfo.InvariantCulture);
          if (digits.Length <= Scale)
          {
              digits = digits.PadLeft(Scale + 1, '0');
          }

          int pointIndex = digits.Length - Scale;
          string text = digits.Substring(0, pointIndex) + "." + digits.Substring(pointIndex);
          return negative ? "-" + text : text;
      }

      /// <inheritdoc />
      public bool Equals(LadybugDecimal other) => Unscaled == other.Unscaled && Scale == other.Scale;

      /// <inheritdoc />
      public override bool Equals(object? obj) => obj is LadybugDecimal other && Equals(other);

      /// <inheritdoc />
      public override int GetHashCode() => (Unscaled, Scale).GetHashCode();

      public static bool operator ==(LadybugDecimal left, LadybugDecimal right) => left.Equals(right);

      public static bool operator !=(LadybugDecimal left, LadybugDecimal right) => !left.Equals(right);
  }
  ```
- [ ] **Run / expect PASS:**
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter FullyQualifiedName~LadybugDecimalTests`
- [ ] **Commit:** `git commit -am "feat(types): add LadybugDecimal value type with exact ToString"`

---

## TASK 2 — `LadybugDecimal.ToDecimal()` / `TryToDecimal()` (in-range projection)

- [ ] **Write the failing test.** Append to `LadybugDecimalTests.cs`:
  ```csharp
      [Fact]
      public void ToDecimal_round_trips_an_in_range_value()
      {
          var d = new LadybugDecimal(new BigInteger(12345), 2);
          Assert.Equal(123.45m, d.ToDecimal());
      }

      [Fact]
      public void ToDecimal_round_trips_a_negative_in_range_value()
      {
          var d = new LadybugDecimal(new BigInteger(-12345), 4);
          Assert.Equal(-1.2345m, d.ToDecimal());
      }

      [Fact]
      public void TryToDecimal_returns_true_for_in_range()
      {
          var d = new LadybugDecimal(new BigInteger(1), 0);
          Assert.True(d.TryToDecimal(out decimal value));
          Assert.Equal(1m, value);
      }

      [Fact]
      public void TryToDecimal_returns_false_when_mantissa_exceeds_decimal_range()
      {
          // 31 nines: larger than decimal.MaxValue (~7.9e28), so projection must fail without throwing.
          var huge = BigInteger.Parse("9999999999999999999999999999999");
          var d = new LadybugDecimal(huge, 0);
          Assert.False(d.TryToDecimal(out decimal value));
          Assert.Equal(0m, value);
      }

      [Fact]
      public void TryToDecimal_returns_false_when_scale_exceeds_decimal_limit()
      {
          // decimal supports at most 28-29 fractional digits; scale 30 is out of range.
          var d = new LadybugDecimal(new BigInteger(1), 30);
          Assert.False(d.TryToDecimal(out _));
      }

      [Fact]
      public void ToDecimal_throws_when_out_of_range()
      {
          var huge = BigInteger.Parse("9999999999999999999999999999999");
          var d = new LadybugDecimal(huge, 0);
          Assert.Throws<System.OverflowException>(() => d.ToDecimal());
      }
  ```
- [ ] **Run / expect FAIL** (methods missing):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter FullyQualifiedName~LadybugDecimalTests`
- [ ] **Implement minimally.** Add to `LadybugDecimal` (before the equality members):
  ```csharp
      /// <summary>
      /// Projects to a CLR <see cref="decimal"/>. The value fits when the mantissa is within the
      /// 96-bit decimal range and <see cref="Scale"/> is 0–28.
      /// </summary>
      /// <exception cref="OverflowException">The value does not fit in a <see cref="decimal"/>.</exception>
      public decimal ToDecimal()
      {
          if (!TryToDecimal(out decimal value))
          {
              throw new OverflowException("The LadybugDecimal value does not fit in a System.Decimal.");
          }

          return value;
      }

      /// <summary>
      /// Attempts to project to a CLR <see cref="decimal"/> without throwing. Returns <c>false</c>
      /// (and <paramref name="value"/> = 0) when the mantissa or scale is out of decimal range.
      /// </summary>
      public bool TryToDecimal(out decimal value)
      {
          value = 0m;

          // decimal stores a 96-bit unsigned mantissa with a scale of 0..28.
          if (Scale > 28)
          {
              return false;
          }

          BigInteger magnitude = BigInteger.Abs(Unscaled);
          if (magnitude > MaxDecimalMantissa)
          {
              return false;
          }

          byte[] bytes = magnitude.ToByteArray(); // little-endian, two's complement (always non-negative here).
          int lo = 0, mid = 0, hi = 0;
          for (int i = 0; i < bytes.Length && i < 12; i++)
          {
              int shift = (i % 4) * 8;
              int word = i / 4;
              switch (word)
              {
                  case 0: lo |= bytes[i] << shift; break;
                  case 1: mid |= bytes[i] << shift; break;
                  default: hi |= bytes[i] << shift; break;
              }
          }

          value = new decimal(lo, mid, hi, Unscaled.Sign < 0, Scale);
          return true;
      }

      // decimal.MaxValue == 79228162514264337593543950335 (2^96 - 1).
      private static readonly BigInteger MaxDecimalMantissa = BigInteger.Parse("79228162514264337593543950335");
  ```
- [ ] **Run / expect PASS:**
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter FullyQualifiedName~LadybugDecimalTests`
- [ ] **Commit:** `git commit -am "feat(types): add LadybugDecimal.ToDecimal/TryToDecimal projection"`

---

## TASK 3 — `LadybugDecimal.Parse(string)` (exact textual round-trip)

- [ ] **Write the failing test.** Append to `LadybugDecimalTests.cs`:
  ```csharp
      [Theory]
      [InlineData("123.45", "12345", 2)]
      [InlineData("-0.005", "-5", 3)]
      [InlineData("42", "42", 0)]
      [InlineData("0.00", "0", 2)]
      [InlineData("-1.2345", "-12345", 4)]
      [InlineData("  7.5  ", "75", 1)]
      public void Parse_reads_unscaled_and_scale(string text, string expectedUnscaled, int expectedScale)
      {
          var d = LadybugDecimal.Parse(text);
          Assert.Equal(BigInteger.Parse(expectedUnscaled), d.Unscaled);
          Assert.Equal((byte)expectedScale, d.Scale);
      }

      [Fact]
      public void Parse_then_ToString_round_trips_exactly()
      {
          Assert.Equal("123.4500", LadybugDecimal.Parse("123.4500").ToString());
          Assert.Equal("-0.005", LadybugDecimal.Parse("-0.005").ToString());
      }

      [Theory]
      [InlineData("")]
      [InlineData("abc")]
      [InlineData("1.2.3")]
      public void Parse_throws_for_invalid_text(string text)
      {
          Assert.Throws<System.FormatException>(() => LadybugDecimal.Parse(text));
      }

      [Fact]
      public void Parse_throws_for_null()
      {
          Assert.Throws<System.ArgumentNullException>(() => LadybugDecimal.Parse(null!));
      }
  ```
- [ ] **Run / expect FAIL** (`Parse` missing):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter FullyQualifiedName~LadybugDecimalTests`
- [ ] **Implement minimally.** Add to `LadybugDecimal` (after `TryToDecimal`):
  ```csharp
      /// <summary>
      /// Parses the exact textual form (e.g. <c>"-12.3400"</c>) into an unscaled mantissa and scale.
      /// Trailing fractional zeros are preserved (they determine the scale). No exponent form.
      /// </summary>
      /// <exception cref="ArgumentNullException"><paramref name="s"/> is null.</exception>
      /// <exception cref="FormatException"><paramref name="s"/> is not a valid fixed-point literal.</exception>
      public static LadybugDecimal Parse(string s)
      {
          if (s is null)
          {
              throw new ArgumentNullException(nameof(s));
          }

          string text = s.Trim();
          if (text.Length == 0)
          {
              throw new FormatException("The decimal text is empty.");
          }

          int dot = text.IndexOf('.');
          byte scale;
          string digits;
          if (dot < 0)
          {
              scale = 0;
              digits = text;
          }
          else
          {
              if (text.IndexOf('.', dot + 1) >= 0)
              {
                  throw new FormatException($"'{s}' has more than one decimal point.");
              }

              int fractionLength = text.Length - dot - 1;
              if (fractionLength > byte.MaxValue)
              {
                  throw new FormatException($"'{s}' has too many fractional digits.");
              }

              scale = (byte)fractionLength;
              digits = text.Substring(0, dot) + text.Substring(dot + 1);
          }

          // BigInteger.Parse rejects an empty mantissa and any non-digit/sign characters.
          BigInteger unscaled = BigInteger.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
          return new LadybugDecimal(unscaled, scale);
      }
  ```
  Add `using System.Numerics;` and `using System.Globalization;` are already present from Task 1.
- [ ] **Run / expect PASS:**
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter FullyQualifiedName~LadybugDecimalTests`
- [ ] **Commit:** `git commit -am "feat(types): add LadybugDecimal.Parse with exact round-trip"`

---

## TASK 4 — `Value` DECIMAL read returns `decimal`-or-`LadybugDecimal` (never a bare string)

The current `Value.ReadDecimal()` (Value.cs lines 201–208) returns `decimal` or falls back to a raw
`string`. Replace the string fallback with `LadybugDecimal`.

- [ ] **Write the failing test.** Add a native-gated test to `test/LadybugDB.Tests/TypeMappingTests.cs`:
  ```csharp
      [SkippableFact]
      public void Decimal_in_range_maps_to_decimal()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              using QueryResult result = conn.Query("RETURN CAST(123.45 AS DECIMAL(10, 2)) AS d");
              object?[] row = result.Rows().Single();

              Assert.Equal(123.45m, Assert.IsType<decimal>(row[0]));
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }

      [SkippableFact]
      public void Decimal_out_of_decimal_range_maps_to_LadybugDecimal_not_string()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              // DECIMAL(38, 0) holds 38 integer digits — far beyond decimal's ~28-29 significant digits.
              using QueryResult result =
                  conn.Query("RETURN CAST(12345678901234567890123456789012345678 AS DECIMAL(38, 0)) AS d");
              object?[] row = result.Rows().Single();

              var big = Assert.IsType<LadybugDecimal>(row[0]);
              Assert.Equal("12345678901234567890123456789012345678", big.ToString());
              Assert.IsNotType<string>(row[0]);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** when native is staged (currently `row[0]` would be a `string` for the
      out-of-range case). When native is absent the tests skip — to truly drive this, run with native
      staged or assert the skip count is 0 with `LADYBUG_REQUIRE_NATIVE=1`:
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~TypeMappingTests.Decimal"`
- [ ] **Implement minimally.** In `src/LadybugDB/Value.cs`, replace `ReadDecimal()` (lines 201–208):
  ```csharp
      private object ReadDecimal()
      {
          EnsureSuccess(Native.ValueGetDecimalAsString(ref _handle, out IntPtr pointer), DataTypeId.Decimal);
          string text = Native.TakeString(pointer) ?? "0";
          LadybugDecimal value = LadybugDecimal.Parse(text);
          return value.TryToDecimal(out decimal representable) ? representable : value;
      }
  ```
  Remove the now-unused `using System.Globalization;` import only if no other usage remains (the
  `decimal.TryParse` call is gone — confirm with `rg "CultureInfo|NumberStyles" src/LadybugDB/Value.cs`;
  `ReadUuid` does NOT use it, so the import can be dropped — but leave it if the build warns are clean
  either way; the SDK will flag an unused `using` as a warning under warning-clean policy, so remove it).
- [ ] **Run / expect PASS** (native staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~TypeMappingTests.Decimal"`
- [ ] **Commit:** `git commit -am "fix(types): DECIMAL reads as decimal-or-LadybugDecimal, never a bare string"`

---

## TASK 5 — `Value.GetDecimal()` deterministic accessor

- [ ] **Write the failing test.** Add to `test/LadybugDB.Tests/TypeMappingTests.cs`:
  ```csharp
      [SkippableFact]
      public void GetDecimal_returns_LadybugDecimal_for_in_range_and_out_of_range()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              using QueryResult result = conn.Query("RETURN CAST(1.50 AS DECIMAL(10, 2)) AS d");
              using FlatTuple tuple = result.Rows() is var _ ? GetFirstTuple(conn, "RETURN CAST(1.50 AS DECIMAL(10, 2)) AS d") : null!;

              // Read via the deterministic accessor on a freshly fetched value.
              using QueryResult r2 = conn.Query("RETURN CAST(1.50 AS DECIMAL(10, 2)) AS d");
              Assert.True(r2.HasNext());
              using FlatTuple t = r2.GetNext();
              using Value v = t.GetValue(0);
              LadybugDecimal d = v.GetDecimal();
              Assert.Equal("1.50", d.ToString());
              Assert.Equal(1.5m, d.ToDecimal());
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
  > NOTE: simplify — drop the dead `GetFirstTuple` line; the test below is the real one. Use exactly:
  ```csharp
      [SkippableFact]
      public void GetDecimal_returns_a_LadybugDecimal()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              using QueryResult result = conn.Query("RETURN CAST(1.50 AS DECIMAL(10, 2)) AS d");
              Assert.True(result.HasNext());
              using FlatTuple tuple = result.GetNext();
              using Value value = tuple.GetValue(0);

              LadybugDecimal d = value.GetDecimal();
              Assert.Equal("1.50", d.ToString());
              Assert.Equal(1.5m, d.ToDecimal());
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (`GetDecimal` missing — does not compile):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~TypeMappingTests.GetDecimal"`
- [ ] **Implement minimally.** In `src/LadybugDB/Value.cs`, add a public method after `GetString()`
      (after line 154) and refactor `ReadDecimal` to reuse it:
  ```csharp
      /// <summary>
      /// Reads a DECIMAL value losslessly as a <see cref="LadybugDecimal"/>, regardless of whether it
      /// fits a CLR <see cref="decimal"/>. The value must be of type DECIMAL.
      /// </summary>
      public LadybugDecimal GetDecimal()
      {
          ThrowIfDisposed();
          EnsureSuccess(Native.ValueGetDecimalAsString(ref _handle, out IntPtr pointer), DataTypeId.Decimal);
          string text = Native.TakeString(pointer) ?? "0";
          return LadybugDecimal.Parse(text);
      }
  ```
  Then replace `ReadDecimal()` body (from Task 4) to call it:
  ```csharp
      private object ReadDecimal()
      {
          LadybugDecimal value = GetDecimal();
          return value.TryToDecimal(out decimal representable) ? representable : value;
      }
  ```
- [ ] **Run / expect PASS** (native staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~TypeMappingTests.GetDecimal"`
- [ ] **Commit:** `git commit -am "feat(types): add Value.GetDecimal deterministic DECIMAL accessor"`

---

## TASK 6 — First-class fixed `ARRAY` read (honor fixed length)

Today both `List` and `Array` route to `ReadList()` (Value.cs lines 127–129, 242–254), which uses
`lbug_value_get_list_size`. The native `lbug_value_get_list_size` returns the per-row element count, so
fixed ARRAY already yields all elements — but WS-C must make the fixed length first-class by validating
it against the logical type's declared `num_elements_in_array` so a malformed read is caught.

- [ ] **Write the failing test.** Add to `test/LadybugDB.Tests/TypeMappingTests.cs`:
  ```csharp
      [SkippableFact]
      public void Fixed_array_maps_to_object_array_of_declared_length()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              conn.Query("CREATE NODE TABLE V(id INT64, vec DOUBLE[3], PRIMARY KEY(id))").Dispose();
              conn.Query("CREATE (:V {id: 1, vec: [1.0, 2.0, 3.0]})").Dispose();

              using QueryResult result = conn.Query("MATCH (v:V) RETURN v.vec");
              object?[] row = result.Rows().Single();

              var array = Assert.IsType<object?[]>(row[0]);
              Assert.Equal(3, array.Length);
              Assert.Equal(new object?[] { 1.0d, 2.0d, 3.0d }, array);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** when native staged only if the fixed length is mis-honored; otherwise this
      pins the contract. Run:
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~TypeMappingTests.Fixed_array"`
- [ ] **Implement minimally.** In `src/LadybugDB/Value.cs`, split ARRAY from LIST in `GetValue()`
      (lines 127–129):
  ```csharp
              case DataTypeId.List:
                  return ReadList();
              case DataTypeId.Array:
                  return ReadArray();
  ```
  Add a dedicated `ReadArray()` after `ReadList()` that validates against the declared fixed length:
  ```csharp
      private object?[] ReadArray()
      {
          // ARRAY is fixed-length: validate the runtime element count against the declared size so a
          // layout mismatch surfaces here rather than as a truncated read.
          EnsureSuccess(Native.ValueGetListSize(ref _handle, out ulong size), DataTypeId.Array);

          ulong declared = GetFixedArraySize();
          if (declared != 0 && declared != size)
          {
              throw new LadybugException(
                  $"Fixed ARRAY length mismatch: declared {declared}, got {size}.");
          }

          var items = new object?[size];
          for (ulong i = 0; i < size; i++)
          {
              EnsureSuccess(Native.ValueGetListElement(ref _handle, i, out LbugValue elementHandle), DataTypeId.Array);
              using var element = new Value(elementHandle);
              items[i] = element.GetValue();
          }

          return items;
      }

      private ulong GetFixedArraySize()
      {
          Native.ValueGetDataType(ref _handle, out LbugLogicalType logicalType);
          try
          {
              return Native.DataTypeGetNumElementsInArray(ref logicalType, out ulong count) == LbugState.Success
                  ? count
                  : 0;
          }
          finally
          {
              Native.DataTypeDestroy(ref logicalType);
          }
      }
  ```
  > `Native.DataTypeGetNumElementsInArray` is the WS-A wrapper for
  > `lbug_data_type_get_num_elements_in_array` (returns `LbugState`, out `ulong`). If its managed
  > signature differs, match it exactly; do NOT add the interop here.
- [ ] **Run / expect PASS** (native staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~TypeMappingTests.Fixed_array"`
- [ ] **Commit:** `git commit -am "feat(types): first-class fixed ARRAY read honoring declared length"`

---

## TASK 7 — First-class tagged `UNION` read

Today UNION routes to `ReadStruct()` (Value.cs lines 130–132), which returns the full physical struct
(`tag` + one field per union member). Make UNION return a first-class tagged value exposing the active
member's tag and value.

- [ ] **Write the failing test.** Add the new record's unit test to a new non-gated file
      `test/LadybugDB.Tests/UnionValueTests.cs` (struct shape is pure managed):
  ```csharp
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests;

  public sealed class UnionValueTests
  {
      [Fact]
      public void Union_exposes_tag_and_value()
      {
          var u = new Union("amount", 42L);
          Assert.Equal("amount", u.Tag);
          Assert.Equal(42L, u.Value);
      }

      [Fact]
      public void Union_equality_is_structural()
      {
          Assert.Equal(new Union("a", 1L), new Union("a", 1L));
          Assert.NotEqual(new Union("a", 1L), new Union("b", 1L));
      }
  }
  ```
  And the native-gated read test in `TypeMappingTests.cs`:
  ```csharp
      [SkippableFact]
      public void Union_value_materializes_as_tagged_union()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              using QueryResult result = conn.Query("RETURN union_value(num := 5) AS u");
              object?[] row = result.Rows().Single();

              var union = Assert.IsType<Union>(row[0]);
              Assert.Equal("num", union.Tag);
              Assert.Equal(5L, union.Value);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (`Union` type missing — does not compile):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter FullyQualifiedName~UnionValueTests`
- [ ] **Implement the record.** Add to `src/LadybugDB/GraphTypes.cs` (after the `RecursiveRel` record):
  ```csharp
  /// <summary>
  /// A tagged UNION value: the name of the active member (<see cref="Tag"/>) and its materialized
  /// <see cref="Value"/>. Ladybug stores a UNION physically as a struct whose first field is the
  /// active member's tag; this surfaces only the active member.
  /// </summary>
  public sealed record Union(string Tag, object? Value);
  ```
- [ ] **Implement the read.** In `src/LadybugDB/Value.cs`, split UNION from STRUCT in `GetValue()`
      (lines 130–132):
  ```csharp
              case DataTypeId.Struct:
                  return ReadStruct();
              case DataTypeId.Union:
                  return ReadUnion();
  ```
  Add `ReadUnion()` after `ReadStruct()`:
  ```csharp
      private Union ReadUnion()
      {
          // Physical layout: field 0 is the UNION tag (a string naming the active member); the active
          // member's value is the struct field whose name equals that tag.
          EnsureSuccess(Native.ValueGetStructNumFields(ref _handle, out ulong count), DataTypeId.Union);

          string tag = string.Empty;
          if (count > 0)
          {
              EnsureSuccess(Native.ValueGetStructFieldValue(ref _handle, 0, out LbugValue tagHandle), DataTypeId.Union);
              using var tagValue = new Value(tagHandle);
              tag = tagValue.GetString() ?? string.Empty;
          }

          object? active = null;
          for (ulong i = 1; i < count; i++)
          {
              EnsureSuccess(Native.ValueGetStructFieldName(ref _handle, i, out IntPtr namePointer), DataTypeId.Union);
              string name = Native.TakeString(namePointer) ?? string.Empty;
              if (name != tag)
              {
                  continue;
              }

              EnsureSuccess(Native.ValueGetStructFieldValue(ref _handle, i, out LbugValue fieldHandle), DataTypeId.Union);
              using var fieldValue = new Value(fieldHandle);
              active = fieldValue.GetValue();
              break;
          }

          return new Union(tag, active);
      }
  ```
  > VERIFY against the staged engine that field 0 is the tag string. If the engine instead exposes the
  > tag as a numeric discriminator, fall back to: read all member fields, return the single non-null
  > one as `(fieldName, value)`. Note this in the commit if the layout differs from the assumption.
- [ ] **Run / expect PASS** (non-gated record test always; native-gated read when staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~UnionValueTests|FullyQualifiedName~TypeMappingTests.Union"`
- [ ] **Commit:** `git commit -am "feat(types): first-class tagged UNION read"`

---

## TASK 8 — `PreparedStatement.Bind(string, decimal)` and `Bind(string, LadybugDecimal)`

- [ ] **Write the failing test.** Create `test/LadybugDB.Tests/BindingRoundTripTests.cs`:
  ```csharp
  using System.Numerics;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests;

  /// <summary>
  /// Native-gated round-trips for the new WS-C parameter binders (DECIMAL, INT128, BLOB, STRUCT, MAP).
  /// They skip when the native library is absent.
  /// </summary>
  public sealed class BindingRoundTripTests
  {
      [SkippableFact]
      public void Bind_decimal_round_trips()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              using PreparedStatement stmt = conn.Prepare("RETURN CAST($d AS DECIMAL(10, 2)) AS d");
              stmt.Bind("d", 12.34m);
              using QueryResult result = stmt.Execute();

              Assert.Equal(12.34m, Assert.IsType<decimal>(System.Linq.Enumerable.Single(result.Rows())[0]));
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }

      [SkippableFact]
      public void Bind_LadybugDecimal_round_trips()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              using PreparedStatement stmt = conn.Prepare("RETURN CAST($d AS DECIMAL(20, 4)) AS d");
              stmt.Bind("d", new LadybugDecimal(new BigInteger(123456789), 4));
              using QueryResult result = stmt.Execute();

              Assert.Equal("12345.6789", System.Linq.Enumerable.Single(result.Rows())[0]?.ToString());
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  }
  ```
- [ ] **Run / expect FAIL** (overloads missing — does not compile):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Bind_decimal|FullyQualifiedName~BindingRoundTripTests.Bind_LadybugDecimal"`
- [ ] **Implement minimally.** In `src/LadybugDB/PreparedStatement.cs`, add overloads after the
      `Bind(string, Interval)` overload (after line 99). Add `using System.Numerics;` at the top:
  ```csharp
      public PreparedStatement Bind(string name, decimal value)
          => Bind(name, ToLadybugDecimal(value));

      public PreparedStatement Bind(string name, LadybugDecimal value)
          => BindValue(name, CreateDecimalValue(value));
  ```
  Add the value-creator helpers near `CreateNativeValue` (after line 207):
  ```csharp
      private static LadybugDecimal ToLadybugDecimal(decimal value)
      {
          // decimal's scale lives in bits 16-23 of the flags word (index 3 of GetBits).
          int[] bits = decimal.GetBits(value);
          byte scale = (byte)((bits[3] >> 16) & 0x7F);
          BigInteger unscaled = new BigInteger(Math.Abs(value) * Pow10(scale));
          // Reconstruct mantissa exactly from the 96-bit integer to avoid float error.
          unscaled = MantissaOf(value);
          return new LadybugDecimal(value < 0 ? -unscaled : unscaled, scale);
      }

      private static BigInteger MantissaOf(decimal value)
      {
          int[] bits = decimal.GetBits(value);
          uint lo = (uint)bits[0];
          uint mid = (uint)bits[1];
          uint hi = (uint)bits[2];
          return (new BigInteger(hi) << 64) | (new BigInteger(mid) << 32) | lo;
      }

      private static decimal Pow10(int n)
      {
          decimal r = 1m;
          for (int i = 0; i < n; i++)
          {
              r *= 10m;
          }

          return r;
      }

      private static IntPtr CreateDecimalValue(LadybugDecimal value)
      {
          // Native create_decimal takes the textual form plus precision and scale; precision must be
          // at least the number of significant digits in the mantissa.
          string text = value.ToString();
          uint scale = value.Scale;
          uint precision = (uint)BigInteger.Abs(value.Unscaled).ToString().TrimStart('0').Length;
          if (precision < scale + 1)
          {
              precision = scale + 1u;
          }

          IntPtr handle = Native.ValueCreateDecimal(text, precision, scale);
          if (handle == IntPtr.Zero)
          {
              throw new LadybugException("Failed to create a DECIMAL parameter value.");
          }

          return handle;
      }
  ```
  > SIMPLIFY: the `ToLadybugDecimal` first draft above has a redundant first assignment — delete the
  > `Math.Abs(...) * Pow10(...)` line and the `Pow10` helper; the final body must be exactly:
  ```csharp
      private static LadybugDecimal ToLadybugDecimal(decimal value)
      {
          int[] bits = decimal.GetBits(value);
          byte scale = (byte)((bits[3] >> 16) & 0x7F);
          BigInteger mantissa = MantissaOf(value);
          return new LadybugDecimal(value < 0 ? -mantissa : mantissa, scale);
      }
  ```
  > `Native.ValueCreateDecimal(string text, uint precision, uint scale)` is the WS-A wrapper for
  > `lbug_value_create_decimal(const char*, uint32_t, uint32_t)`. Match its exact managed signature
  > (the ns2.0 variant takes a `byte[]` via a `ToUtf8` wrapper internally — call the friendly `string`
  > overload WS-A exposes; do not P/Invoke directly).
- [ ] **Run / expect PASS** (native staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Bind_decimal|FullyQualifiedName~BindingRoundTripTests.Bind_LadybugDecimal"`
- [ ] **Commit:** `git commit -am "feat(binding): Bind(decimal) and Bind(LadybugDecimal) via create_decimal"`

---

## TASK 9 — `PreparedStatement.Bind(string, BigInteger)` (INT128)

- [ ] **Write the failing test.** Append to `BindingRoundTripTests.cs`:
  ```csharp
      [SkippableFact]
      public void Bind_BigInteger_round_trips_as_int128()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              var big = BigInteger.Parse("123456789012345678901234567890");
              using PreparedStatement stmt = conn.Prepare("RETURN CAST($v AS INT128) AS v");
              stmt.Bind("v", big);
              using QueryResult result = stmt.Execute();

              object? value = System.Linq.Enumerable.Single(result.Rows())[0];
              Assert.Equal(System.Int128.Parse("123456789012345678901234567890"), value);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (overload missing):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Bind_BigInteger"`
- [ ] **Implement minimally.** Add the overload after `Bind(string, LadybugDecimal)`:
  ```csharp
      public PreparedStatement Bind(string name, BigInteger value)
          => BindValue(name, CreateInt128Value(value));
  ```
  And the helper near the other creators:
  ```csharp
      private static IntPtr CreateInt128Value(BigInteger value)
      {
          // Use the native string parser so the full 128-bit range is honored exactly.
          if (Native.Int128FromString(value.ToString(CultureInfo.InvariantCulture), out LbugInt128 int128) != LbugState.Success)
          {
              throw new LadybugException($"Value {value} is out of INT128 range.");
          }

          IntPtr handle = Native.ValueCreateInt128(int128);
          if (handle == IntPtr.Zero)
          {
              throw new LadybugException("Failed to create an INT128 parameter value.");
          }

          return handle;
      }
  ```
  Add `using System.Globalization;` to `PreparedStatement.cs` (not currently imported).
  > `Native.Int128FromString(string, out LbugInt128)` wraps `lbug_int128_t_from_string`;
  > `Native.ValueCreateInt128(LbugInt128)` wraps `lbug_value_create_int128`. Both are WS-A's. The
  > `LbugInt128` struct (`{ ulong Low; long High; }`) is in `Interop/NativeTypes.cs`.
- [ ] **Run / expect PASS** (native staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Bind_BigInteger"`
- [ ] **Commit:** `git commit -am "feat(binding): Bind(BigInteger) as INT128 via int128_from_string"`

---

## TASK 10 — `PreparedStatement.Bind(string, byte[])` (BLOB)

The engine has no `bind_blob`/`create_blob` C function; a BLOB value is created from a string of bytes
via `lbug_value_create_string` is NOT correct (that creates STRING). The correct path is a default-typed
BLOB value. Confirm the WS-A creator: there is no `lbug_value_create_blob` in `lbug.h`. Use the
documented engine route — bind a BLOB by constructing a BLOB-typed value through
`lbug_value_create_default(blobType)` is also not writable. **Therefore bind BLOB via the string path
that Cypher coerces with `CAST(... AS BLOB)`** is fragile. The robust route is to encode the bytes as a
Cypher `BLOB` hex/`\x` literal string and bind it as STRING, then `CAST`. Pin this contract in the test.

- [ ] **Write the failing test.** Append to `BindingRoundTripTests.cs`:
  ```csharp
      [SkippableFact]
      public void Bind_byte_array_round_trips_as_blob()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              byte[] payload = { 0x00, 0x01, 0xFE, 0xFF, 0x41 };
              using PreparedStatement stmt = conn.Prepare("RETURN CAST($b AS BLOB) AS b");
              stmt.Bind("b", payload);
              using QueryResult result = stmt.Execute();

              var roundTrip = Assert.IsType<byte[]>(System.Linq.Enumerable.Single(result.Rows())[0]);
              Assert.Equal(payload, roundTrip);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (overload missing):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Bind_byte_array"`
- [ ] **Implement minimally.** Add the overload after `Bind(string, BigInteger)`:
  ```csharp
      public PreparedStatement Bind(string name, byte[] value)
      {
          if (value is null)
          {
              throw new ArgumentNullException(nameof(value));
          }

          // Ladybug accepts a BLOB written as a '\xNN...' escaped string literal; the surrounding query
          // is expected to CAST it to BLOB. This avoids depending on a non-existent create_blob C API.
          return Bind(name, ToBlobLiteral(value));
      }

      private static string ToBlobLiteral(byte[] value)
      {
          var builder = new System.Text.StringBuilder(value.Length * 4);
          foreach (byte b in value)
          {
              builder.Append("\\x").Append(b.ToString("X2", CultureInfo.InvariantCulture));
          }

          return builder.ToString();
      }
  ```
  > VERIFY against the staged engine that `CAST('\x00\x01...' AS BLOB)` yields the exact bytes. If the
  > engine instead exposes a dedicated BLOB value creator in this release's `lbug.h`, prefer that
  > (flag WS-A to wrap it) and switch `Bind(byte[])` to `BindValue(name, Native.ValueCreateBlob(...))`.
  > Document the chosen route in the commit message.
- [ ] **Run / expect PASS** (native staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Bind_byte_array"`
- [ ] **Commit:** `git commit -am "feat(binding): Bind(byte[]) as BLOB via escaped literal + CAST"`

---

## TASK 11 — `PreparedStatement.Bind(string, IReadOnlyDictionary<string, object?>)` (STRUCT)

- [ ] **Write the failing test.** Append to `BindingRoundTripTests.cs`:
  ```csharp
      [SkippableFact]
      public void Bind_dictionary_round_trips_as_struct()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              var fields = new System.Collections.Generic.Dictionary<string, object?>
              {
                  ["x"] = 1L,
                  ["y"] = "a",
              };
              using PreparedStatement stmt = conn.Prepare("RETURN $s AS s");
              stmt.Bind("s", fields);
              using QueryResult result = stmt.Execute();

              var dict = Assert.IsType<System.Collections.Generic.Dictionary<string, object?>>(
                  System.Linq.Enumerable.Single(result.Rows())[0]);
              Assert.Equal(1L, dict["x"]);
              Assert.Equal("a", dict["y"]);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (overload missing):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Bind_dictionary"`
- [ ] **Implement minimally.** Add the overload after `Bind(string, byte[])`:
  ```csharp
      public PreparedStatement Bind(string name, IReadOnlyDictionary<string, object?> value)
      {
          if (value is null)
          {
              throw new ArgumentNullException(nameof(value));
          }

          return BindValue(name, CreateStructValue(value));
      }
  ```
  And the helper, mirroring `CreateNativeList`'s allocate/free discipline:
  ```csharp
      private static IntPtr CreateStructValue(IReadOnlyDictionary<string, object?> fields)
      {
          var fieldNamePtrs = new List<IntPtr>(fields.Count);
          var fieldValuePtrs = new List<IntPtr>(fields.Count);
          try
          {
              foreach (KeyValuePair<string, object?> field in fields)
              {
                  fieldNamePtrs.Add(Marshal.StringToCoTaskMemUTF8(field.Key));
                  fieldValuePtrs.Add(CreateNativeValue(field.Value));
              }

              IntPtr[] names = fieldNamePtrs.ToArray();
              IntPtr[] values = fieldValuePtrs.ToArray();
              LbugState state = Native.ValueCreateStruct((ulong)names.Length, names, values, out IntPtr structHandle);
              if (state != LbugState.Success || structHandle == IntPtr.Zero)
              {
                  throw new LadybugException("Failed to create a STRUCT parameter value.");
              }

              return structHandle;
          }
          finally
          {
              foreach (IntPtr ptr in fieldValuePtrs)
              {
                  Native.ValueDestroy(ptr);
              }

              foreach (IntPtr ptr in fieldNamePtrs)
              {
                  Marshal.FreeCoTaskMem(ptr);
              }
          }
      }
  ```
  Add `using System.Runtime.InteropServices;` to `PreparedStatement.cs` (for `Marshal`).
  > `Marshal.StringToCoTaskMemUTF8` exists on ns2.0 (`System.Runtime.InteropServices`). If the WS-A
  > `Native.ValueCreateStruct` signature marshals names as `string[]`/`byte[][]` instead of `IntPtr[]`,
  > match that exact shape and drop the manual `Marshal` calls. Confirm before implementing.
- [ ] **Run / expect PASS** (native staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Bind_dictionary"`
- [ ] **Commit:** `git commit -am "feat(binding): Bind(IReadOnlyDictionary) as STRUCT via create_struct"`

---

## TASK 12 — `PreparedStatement.BindMap(string, IEnumerable<KeyValuePair<object, object?>>)` (MAP)

- [ ] **Write the failing test.** Append to `BindingRoundTripTests.cs`:
  ```csharp
      [SkippableFact]
      public void BindMap_round_trips_as_map()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              var entries = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<object, object?>>
              {
                  new("a", 1L),
                  new("b", 2L),
              };
              using PreparedStatement stmt = conn.Prepare("RETURN $m AS m");
              stmt.BindMap("m", entries);
              using QueryResult result = stmt.Execute();

              var map = Assert.IsType<System.Collections.Generic.Dictionary<object, object?>>(
                  System.Linq.Enumerable.Single(result.Rows())[0]);
              Assert.Equal(1L, map["a"]);
              Assert.Equal(2L, map["b"]);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (`BindMap` missing):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.BindMap"`
- [ ] **Implement minimally.** Add after the STRUCT overload:
  ```csharp
      public PreparedStatement BindMap(string name, IEnumerable<KeyValuePair<object, object?>> value)
      {
          if (value is null)
          {
              throw new ArgumentNullException(nameof(value));
          }

          return BindValue(name, CreateMapValue(value));
      }

      private static IntPtr CreateMapValue(IEnumerable<KeyValuePair<object, object?>> entries)
      {
          var keyPtrs = new List<IntPtr>();
          var valuePtrs = new List<IntPtr>();
          try
          {
              foreach (KeyValuePair<object, object?> entry in entries)
              {
                  keyPtrs.Add(CreateNativeValue(entry.Key));
                  valuePtrs.Add(CreateNativeValue(entry.Value));
              }

              if (keyPtrs.Count == 0)
              {
                  throw new NotSupportedException("Cannot bind an empty MAP parameter; the engine cannot infer its key/value types.");
              }

              IntPtr[] keys = keyPtrs.ToArray();
              IntPtr[] values = valuePtrs.ToArray();
              LbugState state = Native.ValueCreateMap((ulong)keys.Length, keys, values, out IntPtr mapHandle);
              if (state != LbugState.Success || mapHandle == IntPtr.Zero)
              {
                  throw new LadybugException("Failed to create a MAP parameter value.");
              }

              return mapHandle;
          }
          finally
          {
              foreach (IntPtr ptr in valuePtrs)
              {
                  Native.ValueDestroy(ptr);
              }

              foreach (IntPtr ptr in keyPtrs)
              {
                  Native.ValueDestroy(ptr);
              }
          }
      }
  ```
  > `Native.ValueCreateMap(ulong, IntPtr[] keys, IntPtr[] values, out IntPtr)` wraps
  > `lbug_value_create_map`. Match WS-A's exact signature.
- [ ] **Run / expect PASS** (native staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.BindMap"`
- [ ] **Commit:** `git commit -am "feat(binding): BindMap as MAP via create_map"`

---

## TASK 13 — Extend `Bind(object?)` dispatch + `CreateNativeValue` coverage

The runtime dispatch (`Bind(object?)`, lines 110–138) and `CreateNativeValue` (lines 173–207) must route
the new CLR types so dictionary-driven `Connection.Execute(string, IReadOnlyDictionary<...>)` (and any
caller passing `object?`) handles them. `IReadOnlyDictionary` and `byte[]` are caught by the existing
`IEnumerable` arm today and mis-bound — they must be intercepted BEFORE the `IEnumerable` case.

- [ ] **Write the failing test.** Append to `BindingRoundTripTests.cs`:
  ```csharp
      [SkippableFact]
      public void Dispatch_routes_new_types_via_object_bind()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              var parameters = new System.Collections.Generic.Dictionary<string, object?>
              {
                  ["dec"] = 9.99m,
                  ["big"] = BigInteger.Parse("170141183460469231731687303715884105727"), // INT128 max
                  ["blob"] = new byte[] { 0xDE, 0xAD },
                  ["st"] = new System.Collections.Generic.Dictionary<string, object?> { ["k"] = 7L },
              };
              using QueryResult result = conn.Execute(
                  "RETURN CAST($dec AS DECIMAL(10,2)) AS dec, CAST($big AS INT128) AS big, CAST($blob AS BLOB) AS blob, $st AS st",
                  parameters);

              object?[] row = System.Linq.Enumerable.Single(result.Rows());
              Assert.Equal(9.99m, row[0]);
              Assert.Equal(System.Int128.Parse("170141183460469231731687303715884105727"), row[1]);
              Assert.Equal(new byte[] { 0xDE, 0xAD }, Assert.IsType<byte[]>(row[2]));
              Assert.Equal(7L, Assert.IsType<System.Collections.Generic.Dictionary<string, object?>>(row[3])["k"]);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  ```
- [ ] **Run / expect FAIL** (today `byte[]`/`Dictionary` hit the `IEnumerable` arm and mis-bind, and
      `decimal`/`BigInteger` throw `NotSupportedException`):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Dispatch_routes"`
- [ ] **Implement minimally — `Bind(object?)`.** In `Bind(string, object?)` (lines 110–138) add the new
      cases BEFORE `case IEnumerable v`:
  ```csharp
              case decimal v: return Bind(name, v);
              case LadybugDecimal v: return Bind(name, v);
              case BigInteger v: return Bind(name, v);
              case byte[] v: return Bind(name, v);
              case IReadOnlyDictionary<string, object?> v: return Bind(name, v);
              case IEnumerable<KeyValuePair<object, object?>> v: return BindMap(name, v);
  ```
  (Insert these directly after the `case Interval v:` / `case DateOnly v:` block and before
  `case IEnumerable v:` so the more-specific collection types win.)
- [ ] **Implement minimally — `CreateNativeValue`.** In the `CreateNativeValue` switch (lines 175–199)
      add arms BEFORE `IEnumerable v => CreateNativeList(v)`:
  ```csharp
              decimal v => CreateDecimalValue(ToLadybugDecimal(v)),
              LadybugDecimal v => CreateDecimalValue(v),
              BigInteger v => CreateInt128Value(v),
              byte[] v => Native.ValueCreateString(ToBlobLiteral(v)), // BLOB literal; caller CASTs to BLOB
              IReadOnlyDictionary<string, object?> v => CreateStructValue(v),
              IEnumerable<KeyValuePair<object, object?>> v => CreateMapValue(v),
  ```
  > NOTE the `byte[]` arm in `CreateNativeValue` produces a STRING containing the `\xNN` literal, kept
  > consistent with `Bind(byte[])`. If Task 10's VERIFY switched BLOB to a real value creator, use it
  > here too.
- [ ] **Run / expect PASS** (native staged):
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~BindingRoundTripTests.Dispatch_routes"`
- [ ] **Commit:** `git commit -am "feat(binding): dispatch decimal/BigInteger/byte[]/dictionary/map in Bind(object?)"`

---

## TASK 14 — Full-suite regression + both-TFM build gate

- [ ] **Build both TFMs:**
      `dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug`
      Expect: green, warning-clean (SDK analyzers + `GenerateDocumentationFile` are on; no CS1591 because
      it is suppressed, but unused-`using` warnings will fail the warning-clean expectation — fix any).
- [ ] **Run the non-gated tests (no native needed) and confirm they PASS, not skip:**
      `dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug --filter "FullyQualifiedName~LadybugDecimalTests|FullyQualifiedName~UnionValueTests|FullyQualifiedName~StructLayoutTests"`
      Expect: all pass (these never touch native).
- [ ] **Run the full suite.** With native staged (`pwsh -File scripts/build-native-and-test.ps1` or a
      pre-staged `lib/runtimes/<rid>/native`), the gated binding/read tests must PASS. Without native they
      skip — to prove they are not silently broken in CI, run with the hard gate when native is present:
      `$env:LADYBUG_REQUIRE_NATIVE=1; dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug`
- [ ] **Commit (if any cleanup was needed):**
      `git commit -am "test(types): green WS-C suite across both TFMs"`

---

## Self-review / done criteria (tied to WS-C Done column: "§4.2 contract; tests")

- [ ] `LadybugDecimal` exists with the EXACT pinned surface: `BigInteger Unscaled`, `byte Scale`,
      `decimal ToDecimal()` (throws out of range), `bool TryToDecimal(out decimal)`,
      `override string ToString()` (exact textual form), `static LadybugDecimal Parse(string)`.
- [ ] `LadybugDecimal` math/parse/round-trip/out-of-range/negative/scale tests are PLAIN `[Fact]`
      (NOT `SkippableFact`, NOT gated on `TestEnvironment.NativeAvailable`) and pass with no native lib.
- [ ] `Value.GetValue()` for DECIMAL returns `decimal` when representable, else `LadybugDecimal`, and
      NEVER a bare `string` (verified by `Decimal_out_of_decimal_range_maps_to_LadybugDecimal_not_string`).
- [ ] `Value.GetDecimal()` exists and always returns a `LadybugDecimal` (deterministic).
- [ ] Fixed `ARRAY` reads honor the declared length (`ReadArray` validates against
      `DataTypeGetNumElementsInArray`); `UNION` reads return a first-class tagged `Union(Tag, Value)`.
- [ ] `PreparedStatement` has the EXACT pinned overloads: `Bind(string, decimal)`,
      `Bind(string, LadybugDecimal)`, `Bind(string, BigInteger)`, `Bind(string, byte[])`,
      `Bind(string, IReadOnlyDictionary<string, object?>)`, `BindMap(string, IEnumerable<KeyValuePair<object, object?>>)`.
- [ ] `Bind(object?)` dispatch and `CreateNativeValue` route all six new shapes, with `byte[]` and
      `IReadOnlyDictionary`/`KeyValuePair` sequences intercepted BEFORE the generic `IEnumerable` arm.
- [ ] All binding round-trip tests are `SkippableFact` gated on `TestEnvironment.NativeAvailable`.
- [ ] Only WS-C-owned files were modified (`Value.cs`, `PreparedStatement.cs`, `GraphTypes.cs`,
      new `LadybugDecimal.cs`, test files); NO edits to `Interop/Native.*` (WS-A owns those).
      `DataTypeId.cs` was left unchanged (no new enum value was required).
- [ ] `dotnet build LadybugDB.slnx -c Debug` green on both `net10.0` and `netstandard2.0`, warning-clean.
- [ ] `BigInteger` usage compiles on `netstandard2.0` (it ships in `System.Runtime.Numerics`, part of
      the ns2.0 reference assemblies — no extra `PackageReference` needed; confirm the ns2.0 build is green).
