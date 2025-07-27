using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
public class EmbeddingService : IDisposable
{
    private readonly InferenceSession _session;
    private static readonly Lazy<EmbeddingService> _instance =
        new Lazy<EmbeddingService>(() => new EmbeddingService());

    private readonly List<string> _vocabulary;
    private const int MaxSequenceLength = 256;
    private const string UnknownToken = "[UNK]";
    private const string StartToken = "[CLS]";
    private const string EndToken = "[SEP]";

    public static EmbeddingService Instance => _instance.Value;

    private EmbeddingService()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "model.onnx");
        var vocabPath = Path.Combine(AppContext.BaseDirectory, "Models", "vocab.txt");

        _session = new InferenceSession(modelPath);
        _vocabulary = File.ReadAllLines(vocabPath).ToList();
    }

    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        return await Task.Run(() => GenerateEmbedding(text));
    }

    private float[] GenerateEmbedding(string text)
    {
        // Tokenize and convert to IDs
        var tokenIds = Tokenize(text);
        var seqLength = tokenIds.Count;

        // Create input tensors
        var inputIds = new DenseTensor<long>(new[] { 1, seqLength });
        var attentionMask = new DenseTensor<long>(new[] { 1, seqLength });
        var tokenTypeIds = new DenseTensor<long>(new[] { 1, seqLength });

        for (int i = 0; i < seqLength; i++)
        {
            inputIds[0, i] = tokenIds[i];
            attentionMask[0, i] = 1;
            tokenTypeIds[0, i] = 0;
        }

        // Model inputs
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };

        // Run inference
        using var results = _session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        // Mean pooling
        var embedding = new float[outputTensor.Dimensions[2]];
        for (int d = 0; d < embedding.Length; d++)
        {
            float sum = 0;
            for (int i = 0; i < seqLength; i++)
            {
                sum += outputTensor[0, i, d];
            }
            embedding[d] = sum / seqLength;
        }

        // Normalize to unit vector
        var norm = Math.Sqrt(embedding.Sum(x => x * x));
        return embedding.Select(x => (float)(x / norm)).ToArray();
    }

    private List<long> Tokenize(string text)
    {
        var tokens = new List<long>();
        tokens.Add((long)_vocabulary.IndexOf(StartToken));  // [CLS]

        // Simple whitespace tokenization with BPE-like handling
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (tokens.Count >= MaxSequenceLength - 1) break; // -1 for [SEP]

            // Try to match whole words first
            var token = _vocabulary.IndexOf(word);
            if (token >= 0)
            {
                tokens.Add(token);
                continue;
            }

            // Handle subwords
            var current = word;
            while (current.Length > 0)
            {
                var found = false;
                for (int len = Math.Min(current.Length, 20); len > 0; len--)
                {
                    var sub = current.Substring(0, len);
                    if (len < current.Length) sub += "##";

                    var subToken = _vocabulary.IndexOf(sub);
                    if (subToken >= 0)
                    {
                        tokens.Add(subToken);
                        current = current.Substring(len);
                        found = true;
                        break;
                    }
                }

                if (!found || tokens.Count >= MaxSequenceLength - 1)
                {
                    tokens.Add(_vocabulary.IndexOf(UnknownToken));
                    break;
                }
            }
        }

        tokens.Add((long)_vocabulary.IndexOf(EndToken));  // [SEP]
        return tokens;
    }

    public void Dispose() => _session?.Dispose();
}