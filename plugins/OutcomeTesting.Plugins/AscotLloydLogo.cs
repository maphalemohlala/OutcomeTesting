namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The Ascot Lloyd logo drawn top left on every page of the generated PDFs (project owner,
    /// 2026-11-23): app/src/assets/ascot-lloyd-logo.svg rasterised to 420 x 136 on white and
    /// reduced to a 16-colour palette, so the pixels are one byte each and compress to a few
    /// kilobytes. <see cref="Pixels"/> is zlib data, which a PDF reader inflates itself
    /// (/FlateDecode), so the writer needs no image code at all.
    /// shortcut: a raster, not the vector paths; convert the SVG to PDF path operators if the
    /// logo ever has to print much larger than its 80pt width.
    /// </summary>
    public static class AscotLloydLogo
    {
        /// <summary>Pixels across.</summary>
        public const int Width = 420;

        /// <summary>Pixels down.</summary>
        public const int Height = 136;

        /// <summary>The 16 palette colours, RGB, as hex: the PDF's /Indexed lookup string.</summary>
        public const string Palette = "FFFFFFF8FCF4F6F7F8DCF3C5B0ED71B0ED70DEE4E5C2CDCFA5B6B88A9FA2738D90627F83486A6E3B5F6330565B2B5257";

        /// <summary>One palette index per pixel, row by row, zlib-compressed, base64.</summary>
        public const string Pixels =
            "eNrtnemC6ioMgBUdCGvf/21vnbErIQRqWzy3/DynU5WPQMh6u7HH/fF4/uDj+Xw+Ho/7rcEh3Tjk7X8x0ph+ST3amwbdjUPf/ifjTlHqOTUmTc"
            + "JPjLz4hzgIpa0LXXBOQywZdxJSY5Sgmw34ZwhJE+Y/zEG0/GhR+vlpiJJb/JSvmH+lsoRstx4eCkWpHVlSy1+ivgARdD6zy+nQISNSiXKQfp5t"
            + "QDLL32G+AVGGkXQdPgIUQ3q0sG2/V1x4aw5BfgEimpH0XXIUQ2pBlAbF2+gvUb9/92ZfiSj+eV8AaVS8pfwO9VuFDCPhOnKsJenx0zqkQWOwt5"
            + "v9BvX7DxHFyNKI4s28eUgzMDNcefGTUikleSInXs9yHu7fKunH3ogIRtDlhi3e7c6FNN/gpm0vM5Ngxu3EW61E5qo/qsHOQPrVEqzLPjYg6hW0"
            + "xVCo0eRzu93PswHFWy/Uh4Lr++/ChAQmqaMZswrHvjpEHPrKpDZgCsQoPnEZgvRzngouwlx0BqEKovRyGIzEaKIz5FRMCJl7H1NKK2wTI4YYVQ"
            + "nSeZBg+RuzWoNIHsghUtkhpJYx88a5tgoQOrVJGE0+dyKddyS5pQFIZYx2gtCZHPtRz2S5sgpIQqc2CaNJ6sVrEX22K0gRE0cb7XTHZURNqGe/"
            + "c3HhpHRqg9qH00NVbHYnQYr2NiDVb9nN1DlQCrRxAWW03JZ6B461dnzUJ62Fwc0eW0PiMJI8RLGxgcPoeabiPYn+UodIKYHz47xXrl3EaH7Vdz"
            + "Aq5716HVaM9PzB94cOb1zNpvgdMFxEF0MUHUfIGny2KkiIrm0Io90AMLLwq5c67tBtaa1qv5Rsj/lFltqecqltCcg7LDAZuSpGzzNNdfGkYUY7"
            + "lZaxXs12yEx5QK9CyIVTi7WOn7jM7MSIdSCdIEjo4ePS6jdQSp/Ukbej87i5QERSG4CYbvN/ZoTigLT6DUxznqERITqIIudbNsPo8DsSvq2NWo"
            + "OqZSQD1+tuaI8VapqiGVXrDDxGhwtSYoZM0minspaixdRqrgfYiYwjSLIZcXVvU8noeY6PfK0CyJSbZfwfw9pCg+Cehyon6cBmJAKPEXwHI50S"
            + "e5uUg0ELM4JxyGjuDdplcVs2o6yDL6WaMhn9HHsg+dQqhqT6PenDWmalIxu6IvJu3+Fdgs2IpzTEf81l9DjVVBdfWiC5PVI+I51zlDKuW2uZVG"
            + "xGMtSYghplRLghkrvgapVa1F1quA53YMD00ZfM/ZWp2urYjJ4nmOo6a6Jh0z9lvZU4E3nKHfc40gyY8ctyjGSNVtcmI93V/ZbY2RNWmD7KyBYz"
            + "YggSFuXJZXSg0sDTUTH1GXOAO4gZAfd+Zj7MSPpixbtNRlB5jUgEksxN4Ww52olR1taAfmCDjFylWWvlM5qJnFq92py11+UWIG7VaI8R16xFmN"
            + "x6fx3+6Ml6Xdalj6v67TGyXa3tcRUL6eOTWHNNQbvcj3JhLDbxtZpjNPkEZGJAx7QWqCneziynXnG/BcPOIAsZ3VRCcdCpldMcI52VktHWkD/6"
            + "R4/pW3IEO9fMZWXVxccij9FNGFb0ZbOMhM8vdd1xt6xptcNiI83nmmmu3VuXM0Jinh0l2a0xgi7/O0e7F/A9URqNfc1vdi6XSKTiLx84axHGdI"
            + "DgNL1kWmPkOPuYKcgzhwUUwcbreH5YlzGFk+tAoWk1snFGsuNsRqogz1wtDxYd3ZmqPgPQ/2W7PuhV1TgjU+ROnT0mreTI0XTeJSCp6A6APTjq"
            + "zzbrm624fLTNSDBDQmL1uxdAK/Pn0fyij+i60nrE25OOr1uJu/xErQ/l2mYEzB85stSL6UF8e0NynUJsz6ssIqHsQleBhNY1SxlbH1YB+XdZWF"
            + "TCNM7IcTcLs/aZy8EZsfTtDffFmZo+j/cO9i/gu0+KfetZHvckuHfWZm8N9ISlY7SRDGlpv1apwnCbthkp9sVHrq3fM/eZN6+8iT5vGSbrqmGm"
            + "tiwYrdKUgvchY7uZWUxd76C0fzwLT+S2GVl+yRm32hVlgeeMyibz3AdR8xqelFy4TJtmJDu+7rrWGmSJr0kYZlSOIKzUaJAYbGT0cgI2zUgXqE"
            + "XjijXxSc4ItVGO6ZVKPeiB7w0vKUzatc1IlJh4YqOdgMTO5BVqi/FocrNgJauHpI0aEVHL3+h81zgjKKoJJJF9TOp41fvUdMZM8boLPaV1fQYt"
            + "yPvNKuqFvUbfq6FlRqODaMvjEmZpsFj4VlTT9KWs9bmuGih/Xv/W3zXuyXIoo9/K/r7U96VS2TWoJsF2XxDP8IFdM2GtbHfMY2X+H4y+bKy26I"
            + "tRc4DiY/Fi1NSmjOmiF6OWqrCb8MF82IvRrlrCxajFofgFqC5G5xByO+T+X4wOI3QxaqEbyE41NC5GHxOifL3Of4+RaKe9gRAfSVFs2jfxHvz7"
            + "BfTGyybaG7y/CtQEen8VI1MYQejaaUHBy+EUtrsYtc2IiehidCIjXlnii9GJjLjl6y5GpzFip/lejM5iJNzFqHVGursYNc5IhotR64xMdzFqnJ"
            + "HsGmIkxMVo22m0J6N3j/Q+nJARJXgMo1dWEbzSXPbA0ofwwfj2DCPhW2C07pGuxdmM5JRc32ckxeGqYqhaSHThGx5BwlZns973ifE0I+jOZ4T0"
            + "SA/llD7JKIoXiCvhmmyVgUQLwHQsAnxAY9iHUaINnlenMZKWkRORbQuMt9KkPN2QKbN8GqO0OVefxEgHVpJLrsEf1paE9gDBB7S6PRixOtEdyY"
            + "hyAWheheolQ8VMpU0ygpMZ0d/aHM+o4AvRDf6QYgsqdDWM9LmMcqZCfTSjRVXZ0GcVvfryJSDRBZ5MNPFzg07/6lfu+it5fdIfYWPVxH0YmfqC"
            + "mrswmq2ZWd3oefKIjh9GixMNmZ+TSjHDbxe6vFC07u1OZZR3ijhxKKNpzSzvPWKqkBnX1kSlPf4/m9ZY6Tus38jovomRq2mfsCMj1SUbtI36+E"
            + "zXjmUl7sIjo5MfqUtEM+rOZMRRWII8jtF4o8E6uQmkQodOHiRq/fD4bituBzPiNbZMMHJ1fUh2Y6TJdTFCkpEWYPNfRlObd7uM5JZa6TswSjfe"
            + "WeoIOv+ZMTyyzivNKGxltKFJla7si7UXI5W7lcVN+1It4qJWb/S73a563RZGdkNjiD0Y5YsO2+gbWbzSV1QYmv5ybtf7EXuzu1f3ADQHMRq+j8"
            + "kGUJlcec34Xx25cbtd7QxsQbpXmwrdQYw4RfBdVFwLnXwb3V9pBcjtaq/bwIgb1ecPYgSMAoU61edwQTYmkuHvdrV79zekZ2VDPnbk5UGMOE1Z"
            + "VDTVgzJoY5Kzgy1TFdrt6j/a0LK8NUaWcfzJZHH72ccO2OLLbqiLOTGbGd13ZhSOZaQZjABRfTQlNIbetjOM1GZGPM3u0bzOUMkobopk493P0D"
            + "9l37ggtiA9k72Iuy29pBpgNNgUIN18Ktcae9/4Ov6JdK+9P+tjdQZdpjPEQqKRqdq2122MU+Wrdo/ao1AdxIjT5xCwr7QUHBHS+2GqI8u+8d58"
            + "QXpWHoVBHHuHJT/Poh+x0AjRa5bepHtvzJsoUBvudUehOcruLfNyK/GqsosOiWiYBHQcq+lO+UcFu92j7oOP8/Hle+jpxLKZvQrvaJkxqe+bx1"
            + "ew2z3vNVZVe5yvXOdeMESNqFQ7WJHqsDz80sRGum8+bAmkGkHiu8q3M5K5ZWFS4QtiDOwRAcdIB7Xum1deBOleLsH6yNitTJg9EQYEw/RAYp6A"
            + "jHPatz5DEaRncSN7K45kRDe8pOrwj+KTmm0ZKP771jkpg1RqtHPi2DhVTfh+gfQLDxfXJEZD/bnftV7QZkhQ3iN9P0bT3rtO/ZpK++GivbzAAG"
            + "UqSIfu7VR3awUpp4I/HwVV88oQTQHWihp0nOoUNb/oBSKndBcv86cFqr1NDyyDIJUpiNzQH7Bv3isgpTIVjKjLP+I6dcnL5rvZ3qtnSJ+oy7BM"
            + "ydy1e35jt++U3z4J2JVF1zDqQObfkhMlBBKayOdUba7lJkY5FReyjeaSu+3y6A3OuVARAZWtp8o5w++P4jMpLijuoD5neRsjMkmI6q6sspuN+k"
            + "yUGl2X2PMOiNyG97yjhfndlJojt+T+b2REZInRsu2yhnpI4A+2LJKQqMBu2VN3p3e85+OebjtU3XXoU4ySnQ4zBySjgTCO30oo7YGd6GRgiw6I"
            + "ntKznNLtE7VoNjPCN/3sChWMW46IRem1rUNFn/K+gMSCuK+pPXKXJKePYzKeMRyzhsa67xNj+31fkTLtAMWigea73R78fTmoaXj2rpayoYjP/f"
            + "4gRtOF2Xul29h+GM1boJprYZTw92IN6nu6yf0T422yC/KainbETPD8f9e4ndg+RWIGu0uMWmLU97MWkZPFXhPTFKOX3ixXKvUlRq0x+m3FbMxU"
            + "qURf89Igo3qf5DXOYOSvna51RkFds9J42zV/IWrQsrDwTZrrLGq1X7j/c6rq6yhqmVMf8rCvCP0H8G08mw==";
    }
}
