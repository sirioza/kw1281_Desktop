using System.Text;

namespace BitFab.KW1281Test.Blocks
{
    internal class GroupReadResponseWithTextBlock : Block
    {
        public GroupReadResponseWithTextBlock(List<byte> bytes)
            : base(bytes)
        {
            var bodyBytes = new List<byte>(Body);
            while (bodyBytes.Count > 2)
            {
                var subBlockHeader = bodyBytes.Take(3).ToArray();
                bodyBytes = [.. bodyBytes.Skip(3)];

                int subBlockBodyLength = subBlockHeader[2];
                if (bodyBytes.Count < subBlockBodyLength)
                {
                    throw new InvalidOperationException(
                        $"{nameof(GroupReadResponseWithTextBlock)} body ({Utils.DumpBytes(Body)}) contains extra bytes after sub-blocks.");
                }

                var subBlock = new SubBlock
                {
                    BlockType = subBlockHeader[0],
                    Data = subBlockHeader[1],
                    Body = [.. bodyBytes.Take(subBlockBodyLength)]
                };
                bodyBytes = [.. bodyBytes.Skip(subBlockBodyLength)];

                SubBlocks.Add(subBlock);

                if (subBlock.BlockType == 0x8D)
                {
                    var text = Encoding.ASCII.GetString(subBlock.Body, 0, subBlock.Body.Length);
                    _text = [.. text.Split((char)0x03)];
                }
            }

            if (bodyBytes.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(GroupReadResponseWithTextBlock)} body ({Utils.DumpBytes(Body)}) contains extra bytes after sub-blocks.");
            }
        }

        private readonly List<string> _text = [];

        public string GetText(int i)
        {
            if (i >= 0 && i < _text.Count)
            {
                return $"\"{_text[i]}\"";
            }

            return i.ToString();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            foreach (var subBlock in SubBlocks)
            {
                sb.Append(subBlock.ToString());
            }

            return sb.ToString();
        }

        readonly List<SubBlock> SubBlocks = [];

        class SubBlock
        {
            public byte BlockType { get; init; }

            public byte Data { get; init; }

            public byte[] Body { get; init; } = [];

            public override string ToString()
            {
                return BlockType switch
                {
                    0x8D => $"(${BlockType:X2} ${Data:X2} {Encoding.ASCII.GetString(Body, 0, Body.Length).Replace((char)0x03, '|')})",
                    _ => $"(${BlockType:X2} ${Data:X2}{Utils.Dump(Body)})",
                };
            }
        }
    }
}