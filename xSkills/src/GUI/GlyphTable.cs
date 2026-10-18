namespace xSkills.GUI
{
    internal struct BitmapGlyph
    {
        public readonly int X, Y, W, H;
        public BitmapGlyph(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    }

    internal static class GlyphTable
    {
        internal static readonly BitmapGlyph[] Large = {
            new BitmapGlyph(2,2,3,27),new BitmapGlyph(7,2,5,27),new BitmapGlyph(14,2,7,27),new BitmapGlyph(23,2,14,27),new BitmapGlyph(39,2,9,27),
            new BitmapGlyph(50,2,16,27),new BitmapGlyph(68,2,15,27),new BitmapGlyph(85,2,3,27),new BitmapGlyph(90,2,6,27),new BitmapGlyph(98,2,6,27),
            new BitmapGlyph(106,2,9,27),new BitmapGlyph(117,2,10,27),new BitmapGlyph(129,2,5,27),new BitmapGlyph(136,2,10,27),new BitmapGlyph(148,2,5,27),
            new BitmapGlyph(155,2,10,27),new BitmapGlyph(167,2,13,27),new BitmapGlyph(182,2,9,27),new BitmapGlyph(193,2,11,27),new BitmapGlyph(206,2,11,27),
            new BitmapGlyph(219,2,11,27),new BitmapGlyph(232,2,11,27),new BitmapGlyph(2,31,11,27),new BitmapGlyph(15,31,12,27),new BitmapGlyph(29,31,11,27),
            new BitmapGlyph(42,31,11,27),new BitmapGlyph(55,31,5,27),new BitmapGlyph(62,31,5,27),new BitmapGlyph(69,31,11,27),new BitmapGlyph(82,31,10,27),
            new BitmapGlyph(94,31,11,27),new BitmapGlyph(107,31,11,27),new BitmapGlyph(120,31,14,27),new BitmapGlyph(136,31,17,27),new BitmapGlyph(155,31,14,27),
            new BitmapGlyph(171,31,13,27),new BitmapGlyph(186,31,14,27),new BitmapGlyph(202,31,12,27),new BitmapGlyph(216,31,13,27),new BitmapGlyph(231,31,14,27),
            new BitmapGlyph(2,60,16,27),new BitmapGlyph(20,60,7,27),new BitmapGlyph(29,60,9,27),new BitmapGlyph(40,60,16,27),new BitmapGlyph(58,60,13,27),
            new BitmapGlyph(73,60,19,27),new BitmapGlyph(94,60,17,27),new BitmapGlyph(113,60,14,27),new BitmapGlyph(129,60,14,27),new BitmapGlyph(145,60,14,27),
            new BitmapGlyph(161,60,16,27),new BitmapGlyph(179,60,10,27),new BitmapGlyph(191,60,15,27),new BitmapGlyph(208,60,16,27),new BitmapGlyph(226,60,14,27),
            new BitmapGlyph(2,89,19,27),new BitmapGlyph(23,89,13,27),new BitmapGlyph(38,89,15,27),new BitmapGlyph(55,89,12,27),new BitmapGlyph(69,89,6,27),
            new BitmapGlyph(77,89,10,27),new BitmapGlyph(89,89,6,27),new BitmapGlyph(97,89,8,27),new BitmapGlyph(107,89,14,27),new BitmapGlyph(123,89,6,27),
            new BitmapGlyph(131,89,12,27),new BitmapGlyph(145,89,13,27),new BitmapGlyph(160,89,10,27),new BitmapGlyph(172,89,11,27),new BitmapGlyph(185,89,10,27),
            new BitmapGlyph(197,89,10,27),new BitmapGlyph(209,89,12,27),new BitmapGlyph(223,89,15,27),new BitmapGlyph(240,89,7,27),new BitmapGlyph(2,118,6,27),
            new BitmapGlyph(10,118,13,27),new BitmapGlyph(25,118,7,27),new BitmapGlyph(34,118,18,27),new BitmapGlyph(54,118,15,27),new BitmapGlyph(71,118,11,27),
            new BitmapGlyph(84,118,13,27),new BitmapGlyph(99,118,11,27),new BitmapGlyph(112,118,11,27),new BitmapGlyph(125,118,8,27),new BitmapGlyph(135,118,9,27),
            new BitmapGlyph(146,118,14,27),new BitmapGlyph(162,118,11,27),new BitmapGlyph(175,118,15,27),new BitmapGlyph(192,118,10,27),new BitmapGlyph(204,118,14,27),
            new BitmapGlyph(220,118,10,27),new BitmapGlyph(232,118,9,27),new BitmapGlyph(243,118,3,27),new BitmapGlyph(2,147,9,27),new BitmapGlyph(13,147,11,27),

        };
        internal static readonly BitmapGlyph[] Small = {
            new BitmapGlyph(2,2,4,20),new BitmapGlyph(8,2,4,20),new BitmapGlyph(14,2,5,20),new BitmapGlyph(21,2,11,20),new BitmapGlyph(34,2,8,20),
            new BitmapGlyph(44,2,11,20),new BitmapGlyph(57,2,11,20),new BitmapGlyph(70,2,2,20),new BitmapGlyph(74,2,5,20),new BitmapGlyph(81,2,5,20),
            new BitmapGlyph(88,2,7,20),new BitmapGlyph(97,2,8,20),new BitmapGlyph(107,2,3,20),new BitmapGlyph(112,2,7,20),new BitmapGlyph(121,2,2,20),
            new BitmapGlyph(125,2,7,20),new BitmapGlyph(134,2,9,20),new BitmapGlyph(145,2,6,20),new BitmapGlyph(153,2,7,20),new BitmapGlyph(162,2,7,20),
            new BitmapGlyph(171,2,8,20),new BitmapGlyph(181,2,7,20),new BitmapGlyph(190,2,8,20),new BitmapGlyph(200,2,8,20),new BitmapGlyph(210,2,8,20),
            new BitmapGlyph(220,2,8,20),new BitmapGlyph(230,2,2,20),new BitmapGlyph(234,2,3,20),new BitmapGlyph(239,2,8,20),new BitmapGlyph(2,24,6,20),
            new BitmapGlyph(10,24,8,20),new BitmapGlyph(20,24,8,20),new BitmapGlyph(30,24,10,20),new BitmapGlyph(42,24,11,20),new BitmapGlyph(55,24,10,20),
            new BitmapGlyph(67,24,10,20),new BitmapGlyph(79,24,10,20),new BitmapGlyph(91,24,9,20),new BitmapGlyph(102,24,9,20),new BitmapGlyph(113,24,10,20),
            new BitmapGlyph(125,24,11,20),new BitmapGlyph(138,24,4,20),new BitmapGlyph(144,24,5,20),new BitmapGlyph(151,24,12,20),new BitmapGlyph(165,24,10,20),
            new BitmapGlyph(177,24,13,20),new BitmapGlyph(192,24,12,20),new BitmapGlyph(206,24,10,20),new BitmapGlyph(218,24,10,20),new BitmapGlyph(230,24,11,20),
            new BitmapGlyph(2,46,12,20),new BitmapGlyph(16,46,8,20),new BitmapGlyph(26,46,10,20),new BitmapGlyph(38,46,10,20),new BitmapGlyph(50,46,13,20),
            new BitmapGlyph(65,46,13,20),new BitmapGlyph(80,46,12,20),new BitmapGlyph(94,46,12,20),new BitmapGlyph(108,46,10,20),new BitmapGlyph(120,46,5,20),
            new BitmapGlyph(127,46,7,20),new BitmapGlyph(136,46,5,20),new BitmapGlyph(143,46,6,20),new BitmapGlyph(151,46,9,20),new BitmapGlyph(162,46,5,20),
            new BitmapGlyph(169,46,8,20),new BitmapGlyph(179,46,9,20),new BitmapGlyph(190,46,7,20),new BitmapGlyph(199,46,8,20),new BitmapGlyph(209,46,8,20),
            new BitmapGlyph(219,46,7,20),new BitmapGlyph(228,46,8,20),new BitmapGlyph(238,46,10,20),new BitmapGlyph(250,46,4,20),new BitmapGlyph(2,68,4,20),
            new BitmapGlyph(8,68,9,20),new BitmapGlyph(19,68,4,20),new BitmapGlyph(25,68,12,20),new BitmapGlyph(39,68,10,20),new BitmapGlyph(51,68,9,20),
            new BitmapGlyph(62,68,9,20),new BitmapGlyph(73,68,8,20),new BitmapGlyph(83,68,8,20),new BitmapGlyph(93,68,7,20),new BitmapGlyph(102,68,7,20),
            new BitmapGlyph(111,68,9,20),new BitmapGlyph(122,68,8,20),new BitmapGlyph(132,68,12,20),new BitmapGlyph(146,68,9,20),new BitmapGlyph(157,68,9,20),
            new BitmapGlyph(168,68,8,20),new BitmapGlyph(178,68,7,20),new BitmapGlyph(187,68,2,20),new BitmapGlyph(191,68,7,20),new BitmapGlyph(200,68,9,20),

        };
    }
}
