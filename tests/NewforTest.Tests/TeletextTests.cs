// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Numerics;
using NewforTest.Core;

namespace NewforTest.Tests;

public class TeletextTests
{
    [Theory]
    [InlineData(0x0, 0x15)]
    [InlineData(0x1, 0x02)]
    [InlineData(0x2, 0x49)]
    [InlineData(0x3, 0x5E)]
    [InlineData(0x4, 0x64)]
    [InlineData(0x5, 0x73)]
    [InlineData(0x6, 0x38)]
    [InlineData(0x7, 0x2F)]
    [InlineData(0x8, 0xD0)]
    [InlineData(0x9, 0xC7)]
    [InlineData(0xA, 0x8C)]
    [InlineData(0xB, 0x9B)]
    [InlineData(0xC, 0xA1)]
    [InlineData(0xD, 0xB6)]
    [InlineData(0xE, 0xFD)]
    [InlineData(0xF, 0xEA)]
    public void Hamming84_matches_ets_300_706_table(int nibble, int expected) =>
        Assert.Equal((byte)expected, Teletext.Hamming84(nibble));

    [Fact]
    public void Hamming84_codes_differ_from_each_other_in_at_least_four_bits()
    {
        // The defining property of Hamming 8/4: any two codes are distance >= 4 apart
        for (int a = 0; a < 16; a++)
            for (int b = a + 1; b < 16; b++)
                Assert.True(BitOperations.PopCount((uint)(Teletext.Hamming84(a) ^ Teletext.Hamming84(b))) >= 4);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void Hamming84_rejects_values_outside_a_nibble(int nibble) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Teletext.Hamming84(nibble));

    [Theory]
    [InlineData(0x20, 0x20)] // space already odd
    [InlineData(0x0A, 0x8A)] // end box
    [InlineData(0x0B, 0x0B)] // start box
    [InlineData(0x0C, 0x8C)] // normal height
    [InlineData(0x0D, 0x0D)] // double height
    [InlineData(0x0F, 0x8F)] // BUILD command
    [InlineData(0x18, 0x98)] // CLEAR command
    [InlineData(0x41, 0xC1)] // 'A'
    [InlineData(0xC1, 0xC1)] // existing bit 7 is recomputed, not kept
    [InlineData(0x80, 0x80)] // 0x00 -> needs parity bit
    public void OddParity_sets_bit_7_when_needed(int value, int expected) =>
        Assert.Equal((byte)expected, Teletext.OddParity((byte)value));

    [Fact]
    public void OddParity_always_gives_an_odd_bit_count_and_keeps_the_low_7_bits()
    {
        for (int v = 0; v < 256; v++)
        {
            byte result = Teletext.OddParity((byte)v);
            Assert.Equal(1, BitOperations.PopCount(result) % 2);
            Assert.Equal(v & 0x7F, result & 0x7F);
        }
    }

    [Theory]
    [InlineData('A', 0x41)]
    [InlineData(' ', 0x20)]
    [InlineData('~', 0x7E)]
    [InlineData('£', 0x23)]
    [InlineData('é', 0x3F)] // not yet mapped -> '?'
    [InlineData('\n', 0x3F)] // control characters must not reach the row as attributes
    [InlineData('\u000B', 0x3F)]
    public void EncodeCharacter_maps_to_7_bit_codes(char c, int expected) =>
        Assert.Equal((byte)expected, Teletext.EncodeCharacter(c));
}
