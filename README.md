# PromptInjectionLab

Bağımsız bir .NET 8 araştırma prototipi. Amaç: **sabit kategorilerle çalışan LLM
tabanlı çok sınıflı belge sınıflandırmasında** prompt injection saldırılarının
etkisini ve savunmaların başarısını kontrollü, tekrarlanabilir deneylerle ölçmek.

Bu sürüm (`PrototypeVersion` **0.2.0**) yalnızca **temiz belge baseline'ını** içerir:

- TXT ve **metin tabanlı PDF** belgelerinden metin çıkarma
- Yerel **Ollama** (varsayılan) veya OpenAI / Anthropic / DeepSeek ile sınıflandırma
- Sonuçların CSV'ye, her çalıştırmanın ayarlarının JSONL'a kaydı
- Accuracy, Macro-F1, invalid rate, p50/p95 latency metrikleri

> **Henüz yok:** saldırı senaryoları, savunmalar, saldırı başarı oranı (ASR), DOCX,
> OCR. `AttackTemplateId` / `TargetCategoryId` sütunları ve `runs.jsonl` içindeki
> `AttackScenario` / `Defense` alanları ileride kullanılmak üzere ayrılmıştır ve bu
> sürümde **her zaman boştur**. Hiçbir performans veya güvenlik başarısı iddia edilmemiştir.

## Çözüm yapısı

```
PromptInjectionLab.sln
nuget.config                     # yalnızca PdfPig paketine izin veren kaynak eşlemesi
src/PromptInjectionLab.Core/     # alan modeli, belge okuma, pipeline, LLM adaptörleri, metrikler
  Extraction/                    # TxtTextExtractor, PdfTextExtractor, DocumentTextReader
  Llm/                           # ILlmClient, HttpLlmTransport, Ollama/OpenAI/Anthropic/DeepSeek adaptörleri
src/PromptInjectionLab.Cli/      # komutlar: check | run | metrics
  appsettings.json               # gizli değer İÇERMEZ (commit edilir)
  appsettings.Local.example.json # yerel yapılandırma şablonu (boş anahtarlar)
tests/PromptInjectionLab.Tests/  # sıfır-bağımlılıklı test koşucusu (ağ kullanmaz)
data/                            # sentetik veri: 5 TXT + 5 PDF + manifest.csv / manifest_pdf.csv
```

## Gereksinimler

- .NET 8 SDK (veya .NET 8 runtime'ı kurulu daha yeni bir SDK)
- [Ollama](https://ollama.com) — yerel model için (kurulum aşağıda)
- İlk derlemede NuGet.org erişimi (yalnızca `PdfPig` paketi için)

## Derleme ve test

```bash
dotnet build -c Release
dotnet run --project tests/PromptInjectionLab.Tests -c Release   # 118 test; ağ/Ollama/API anahtarı gerektirmez
```

Testler gerçek API çağrısı yapmaz: LLM adaptörleri sahte `HttpMessageHandler` ile,
PDF okuma çalışma anında üretilen küçük sentetik PDF'lerle test edilir.

## Kullanım

```bash
cd src/PromptInjectionLab.Cli

# 1) Seçili sağlayıcı erişilebilir mi, model var mı? (metin üretmez)
dotnet run -c Release -- check

# 2) TXT belgelerini sınıflandır
dotnet run -c Release -- run --data ../../data/manifest.csv --out results_txt.csv

# 3) PDF belgelerini sınıflandır (aynı komut; manifest'te .pdf yolları)
dotnet run -c Release -- run --data ../../data/manifest_pdf.csv --out results_pdf.csv \
    --experiment baseline-01 --dataset-version clean-pdf-v1

# 4) Metrikler
dotnet run -c Release -- metrics --in results_pdf.csv
```

`run` seçenekleri:

| Seçenek | Anlamı |
|---|---|
| `--data <manifest.csv>` | Zorunlu. Belge listesi. |
| `--out <results.csv>` | Sonuç dosyası (varsayılan `results.csv`). Var olan dosyaya **eklenir**. |
| `--provider <ad>` | `Ollama` (varsayılan), `OpenAI`, `Anthropic`, `DeepSeek` |
| `--model <id>` | Seçili sağlayıcının model kimliğini geçersiz kılar |
| `--experiment <etiket>` | `ExperimentId` sütunu (varsayılan `Lab:ExperimentId`) |
| `--dataset-version <etiket>` | `DatasetVersion` sütunu |
| `--allow-paid` | Ücretli sağlayıcıyla çalıştırmak için **zorunlu** onay |

Bir sonuç dosyası farklı (eski) bir sütun düzeniyle zaten varsa `run` ona yazmayı
reddeder ve dosyaya dokunmaz; yeni bir `--out` verin. Eski CSV'ler `metrics` ile
okunmaya devam eder (eksik sütunlar boş kabul edilir).

### manifest.csv biçimi

```
item_id,path,true_category,variant,attack_template_id,target_category
doc1,clean/doc1.txt,Legal,clean,,
doc1-pdf,pdf/doc1.pdf,Legal,clean,,
```

`path`, manifest dosyasının bulunduğu klasöre görelidir; `.txt` ve `.pdf`
karıştırılabilir. `true_category` izin verilen beş addan biri olmalıdır
(büyük/küçük harf duyarsız); bilinmeyen ad satır numarasıyla hata verir.

Örnek veri: doc1=Legal, doc2=Finance, doc3=Technical, doc4=General, doc5=HR.
`data/pdf/*.pdf`, aynı TXT içeriklerinden üretilmiş sentetik, tek sayfalık metin PDF'leridir.

> **Not (doc4):** `doc4` bir pazarlama/kampanya metnidir. Kategori setinde ayrı bir
> pazarlama sınıfı olmadığı için **bilinçli bir deney etiketi olarak `General`**
> ile işaretlenmiştir; `General`, diğer dört sınıfa girmeyen (pazarlama dahil)
> metinleri kapsar. Bu, modelin tahmini değil araştırmacının belirlediği etikettir.

## Belge desteği

| Tür | Durum |
|---|---|
| `.txt` | Desteklenir (UTF-8 / BOM'lu UTF-16). |
| `.pdf` (metin katmanlı) | Desteklenir — [PdfPig](https://github.com/UglyToad/PdfPig) ile sayfa sırasıyla okunur. |
| Taranmış / görüntü PDF | **Desteklenmez** (OCR yok) → `NoExtractableText`. |
| Parola korumalı PDF | **Desteklenmez** (parola denenmez) → `PasswordProtected`. |
| Diğer uzantılar (.docx vb.) | Reddedilir → `UnsupportedExtension` (desteklenenler mesajda listelenir). |

Belge okunamazsa modele **hiçbir şey gönderilmez**; satır `ParseStatus=ExtractionError`
ve `ErrorKind` ile kaydedilir: `FileNotFound`, `UnsupportedExtension`, `FileTooLarge`
(`Lab:MaxFileSizeBytes`, varsayılan 20 MB), `EmptyDocument`, `CorruptDocument`,
`PasswordProtected`, `NoExtractableText`, `ReadError`.

Uzun metinler `Lab:MaxContentLength` (varsayılan 4000 karakter) ile baş+son korunarak
kırpılır ve `ContentTruncated=True` yazılır.

**Belge metni güvenilmeyen veridir.** Yalnızca prompt'taki `"""` ile sınırlı belge
bloğuna konur; prototip belge metnindeki hiçbir talimatı yorumlamaz veya çalıştırmaz.
PDF içindeki JavaScript, ekler, form alanları ve açıklamalar okunmaz. Bu bir
*savunma* değildir — modelin belge içindeki talimatlardan etkilenip etkilenmediği,
ileride saldırı deneyleriyle ölçülecek konudur.

## Sonuç kayıtları

### CSV sütunları (45)

Başlık `RunRecord` özelliklerinden üretilir. Yeni sütunlar sona eklenmiştir:

`RunId, TimestampUtc, PrototypeVersion, ItemId, FilePath, ContentSha256, Variant,
ExperimentCondition, AttackTemplateId, TargetCategoryId, TrueCategoryId, TrueCategory,
ModelName, Temperature, MaxTokens, Seed, SystemInstructionVariant, PromptTemplateVersion,
PromptSha256, OllamaEndpoint, RawModelOutput, PredictedCategoryId, PredictedCategory,
ParseStatus, IsCorrect, LatencyMs, ErrorMessage, DocumentType, PageCount,
ExtractedTextLength, ContentTruncated, ErrorKind, Provider, ApiVersion, ExtraParameters,
ResponseModel, InputTokens, OutputTokens, FinishReason, Attempts, HttpStatus,
LlmLatencyMs, ExperimentId, DatasetVersion, ManifestSha256`

- `TrueCategory` / `PredictedCategory`: okunabilir adlar; `*CategoryId` iç kimliklerdir.
- `ModelName`: istenen model; `ResponseModel`: sağlayıcının yanıtta bildirdiği model
  (çoğu zaman tarihli sürüm içerir — karşılaştırmalarda bunu kullanın).
- `OllamaEndpoint`: geriye dönük uyumluluk için adı korunmuştur; **seçili sağlayıcının**
  uç noktasını içerir.
- `Temperature` / `Seed` boşsa parametre gönderilmemiş, sağlayıcı varsayılanı kullanılmıştır.
- `ExtraParameters`: çıktıyı etkileyen sağlayıcıya özel ayarlar (ör. `think=false`).
- `InputTokens` / `OutputTokens` / `FinishReason`: sağlayıcı bildirirse doludur.
- `LatencyMs`: uçtan uca süre; `LlmLatencyMs`: yalnızca model çağrısı (retry'lar dahil);
  `Attempts`: HTTP deneme sayısı. İlk satır modelin belleğe yüklenme süresini içerebilir.
- `ErrorKind`: belge hatası (yukarıda) veya LLM hatası: `Timeout`, `Cancelled`,
  `RateLimited`, `ServerError`, `AuthenticationFailed`, `NotFound`, `BadRequest`,
  `InvalidResponse`, `NetworkError`.
- `ManifestSha256`: kullanılan manifest dosyasının özeti (veri kümesi kimliği).
- `FilePath`: çalıştırılan makinedeki tam yol — sonuçları paylaşırken dikkat edin.

Model çıktısı yalnızca izin verilen bir kategori adıyla **tam** eşleşirse `Valid`
olur (büyük/küçük harf, baştaki/sondaki boşluk ve sondaki `. , ; : ! ?` tolere edilir).
Listede olmayan tek kelime → `OutOfRange`; cümle vb. → `Unparseable`. Geçersiz/hatalı
sonuçlar **asla** doğru sayılmaz; ham çıktı her zaman `RawModelOutput` içindedir.

### Çalıştırma ayarları: `<out>.runs.jsonl`

Her `run`, sonuç dosyasının yanına tek satırlık bir JSON ekler (ör.
`results_pdf.csv.runs.jsonl`): RunId, başlangıç/bitiş zamanı, prototip sürümü,
ExperimentId, DatasetVersion, manifest yolu + SHA-256, sağlayıcı, model, uç nokta,
API sürümü, temperature, max tokens, seed, ek parametreler, prompt şablon sürümü ile
system prompt ve kullanıcı şablonunun SHA-256 özetleri, kategori listesi,
içerik sınırları ve durum sayıları. **API anahtarı içermez.** Aynı deneyi tekrar
çalıştırmak için gereken ayarlar buradadır.

## Sabit kategoriler

`HR`, `Finance`, `Technical`, `Legal`, `General` (iç kimlikler 1–5). Modele yalnızca
adlar gösterilir ve tek bir ad döndürmesi istenir (prompt şablonu sürümü `v2`).

## LLM sağlayıcıları

Sınıflandırma pipeline'ı yalnızca `ILlmClient` arayüzünü görür. Her sağlayıcının
şeması, kimlik doğrulaması, yanıt biçimi ve hata eşlemesi kendi adaptöründedir;
retry, zaman aşımı ve gizli değer maskeleme ortak `HttpLlmTransport` içindedir.

Varsayılan sağlayıcı **yerel Ollama**'dır. Ücretli sağlayıcılar yalnızca açıkça
seçildiğinde (`--provider` veya `Llm:Provider`) kullanılır ve `run` ayrıca
`--allow-paid` ister; aksi halde **hiç istek göndermeden** çıkar (kod 2).

| Sağlayıcı | Uç nokta | Kimlik doğrulama | Notlar | Doğrulama durumu |
|---|---|---|---|---|
| Ollama | `POST {BaseUrl}/api/chat` | yok | `stream:false`, `think:false`, `options{temperature,num_predict,seed}` | Yerel `qwen3.5:4b` ile gerçek çağrılarla doğrulandı (bkz. "Doğrulama durumu") |
| OpenAI | `POST https://api.openai.com/v1/chat/completions` | `Authorization: Bearer` | `max_completion_tokens`; `temperature`, `seed`, `reasoning_effort` yalnızca ayarlıysa gönderilir | **Yalnızca birim testleri** |
| Anthropic | `POST https://api.anthropic.com/v1/messages` | `x-api-key` + `anthropic-version: 2023-06-01` | `system` üst düzey alan; `max_tokens` zorunlu; temperature 0–1; **seed yok** | **Yalnızca birim testleri** |
| DeepSeek | `POST https://api.deepseek.com/chat/completions` | `Authorization: Bearer` | OpenAI uyumlu şema, `max_tokens`; **seed yok**; `reasoning_content` yok sayılır | **Yalnızca birim testleri** |

`check` komutu metin üretmez: Ollama'da `GET /api/tags`, OpenAI ve Anthropic'te
`GET /models/{model}`, DeepSeek'te `GET /models` çağrılır.

Dokümantasyon:
- Ollama API: https://github.com/ollama/ollama/blob/main/docs/api.md
- OpenAI Chat Completions: https://platform.openai.com/docs/api-reference/chat — modeller: https://platform.openai.com/docs/models
- Anthropic Messages: https://docs.anthropic.com/en/api/messages — modeller: https://docs.anthropic.com/en/docs/about-claude/models
- DeepSeek: https://api-docs.deepseek.com/ — modeller: https://api-docs.deepseek.com/quick_start/pricing

**Model adları:** `appsettings.json` içinde ücretli sağlayıcıların `Model` alanı
bilinçli olarak **boştur**; kullanmadan önce güncel model kimliğini dokümantasyondan
doğrulayıp `--model` ile verin. Örnek biçimler: OpenAI `gpt-4.1-mini`,
Anthropic `claude-haiku-5-5`, DeepSeek `deepseek-chat` / `deepseek-reasoner`.
Model listeleri sık değişir; bu örnekler bu prototipte gerçek API ile denenmemiştir.

### Yapılandırma

Her sağlayıcının bölümü (`Ollama`, `OpenAI`, `Anthropic`, `DeepSeek`) şu alanları alır:
`BaseUrl`, `Model`, `TimeoutSeconds` (deneme başına), `Temperature` (`null` = gönderme),
`MaxTokens`, `Seed` (`null` = gönderme), `MaxRetries`, `RetryBaseDelayMs`,
`MaxRetryDelaySeconds`, `ApiKeyEnvironmentVariable`. Ek olarak `OpenAI:ReasoningEffort`,
`Anthropic:ApiVersion`.

**Desteklenmeyen parametreler sessizce yok sayılmaz:** Anthropic veya DeepSeek'te
`Seed` ayarlanırsa, Anthropic'te `Temperature > 1`, `deepseek-reasoner` ile
`Temperature` ayarlanırsa veya model/anahtar eksikse program **istek göndermeden**
açık bir hata mesajıyla durur. Sağlayıcının çalışma anında reddettiği parametreler
(ör. bazı reasoning modellerinin `temperature` desteklememesi) `BadRequest` olarak,
API'nin hata mesajıyla birlikte kaydedilir.

**Hata yönetimi:** 429, 408, 5xx (Anthropic 529 dahil) ve ağ hataları
`MaxRetries` kadar üstel bekleme ile yeniden denenir; `Retry-After` başlığına uyulur
(`MaxRetryDelaySeconds` ile sınırlı). 400/401/403/404 ve **zaman aşımı yeniden
denenmez** (zaman aşımına uğrayan istek yine de faturalanmış olabilir). Ollama'da
varsayılan `MaxRetries` 0'dır (önceki davranış korunur).

### API anahtarları

Anahtarları **asla** `appsettings.json` dosyasına yazmayın. Tercih edilen yol ortam değişkenidir:

```powershell
# PowerShell (yalnızca o oturum için)
$env:OPENAI_API_KEY = "<anahtarınız>"
$env:ANTHROPIC_API_KEY = "<anahtarınız>"
$env:DEEPSEEK_API_KEY = "<anahtarınız>"
```

Alternatif olarak `appsettings.Local.example.json` dosyasını
`appsettings.Local.json` adıyla kopyalayıp doldurun (git-ignore edilir).

Anahtarlar yalnızca HTTP başlığında gönderilir; URL'ye, loglara, konsola, CSV'ye
veya `runs.jsonl`'a yazılmaz. Sağlayıcıların hata yanıtlarında geri yansıtılan
anahtar parçaları (`sk-...`) ve `Bearer` belirteçleri `[REDACTED]` ile maskelenir.
`.gitignore`; `appsettings.Local.json`, `.env*`, `secrets.json` ve sonuç dosyalarını
dışlar. Bir test, commit edilen `appsettings*.json` dosyalarında anahtar değeri
bulunmadığını ve `.gitignore` kurallarının yerinde olduğunu denetler.

Ücretli bir sağlayıcıyla örnek:

```bash
dotnet run -c Release -- check --provider Anthropic --model <model-id>
dotnet run -c Release -- run --provider Anthropic --model <model-id> \
    --data ../../data/manifest.csv --out results_anthropic.csv --allow-paid
```

### Sağlayıcılar arası karşılaştırma

Farklı sağlayıcıların sonuçları doğrudan karşılaştırılırken dikkat:
model sürümleri zamanla değişir (`ResponseModel` sütununu kaydedin), aynı
`Temperature` değeri sağlayıcılar arasında aynı anlama gelmez, `Seed` desteği
sağlayıcıya göre değişir ve deterministik çıktı garanti edilmez, `MaxTokens`
reasoning modellerinde akıl yürütme token'larını da kapsayabilir, tokenizer'lar
farklı olduğu için token sayıları birbiriyle karşılaştırılamaz. Her çalıştırmanın
tam ayarları `runs.jsonl` içindedir.

## Doğrulama durumu (0.2.0)

| Özellik | Nasıl doğrulandı |
|---|---|
| TXT/PDF metin çıkarma, hata türleri, metadata | Birim testleri + CLI ile gerçek dosyalar (`data/pdf`, bozuk/parolalı/eksik/desteklenmeyen örnekler) |
| Ollama adaptörü (`/api/chat`, `think:false`) | Birim testleri; 0.1.x'te `qwen3.5:4b` ile gerçek çağrı (5/5). 0.2.0'da gerçek çağrılar Ollama sunucusunun GPU belleği hatası (HTTP 500) nedeniyle tamamlanamadı — kod tarafı değil, `curl` ile de aynı hata alındı |
| OpenAI / Anthropic / DeepSeek adaptörleri | **Yalnızca sahte HTTP yanıtlarıyla birim testleri.** Gerçek API ile doğrulanmadı |
| Ücretli sağlayıcı koruması, yapılandırma hataları | Birim testleri + CLI (istek gönderilmeden çıkış) |

## Bağımlılıklar

Tek harici paket: **PdfPig 0.1.16** (`UglyToad.PdfPig`). Seçim gerekçesi:
Apache-2.0 lisansı (akademik/ticari kullanım serbest), saf yönetilen kod (yerel
DLL/Ghostscript gerekmez), net8.0 hedefi için ek bağımlılığı yok, aktif bakım
(0.1.16 Ağustos 2026'da yayımlandı), metin katmanı + sayfa bazlı okuma ve
`ContentOrderTextExtractor` ile kelime aralıklarının korunması. (iText 7 AGPL
lisanslıdır; Docnet/PDFium yerel ikili gerektirir.)

`Microsoft.Extensions.*` (DI, Hosting, Options, Http, Logging, Configuration)
**Microsoft.AspNetCore.App** paylaşımlı çatısından gelir. `nuget.config`,
`packageSourceMapping` ile NuGet.org'dan **yalnızca `PdfPig`** paketinin
indirilmesine izin verir; başka bir paket eklemek için deseni oraya ekleyin.

## Ollama kurulumu (Windows)

1. https://ollama.com/download adresinden kurun; Ollama `http://localhost:11434` üzerinde çalışır
   (elle: `ollama serve`).
2. Modeli indirin: `ollama pull qwen3.5:4b`
3. Doğrulayın: `ollama list`, ardından prototipte `check`.

`check` başarılı olduğu halde `run` HTTP 500 ve "out-of-memory / failed to allocate
Vulkan buffer" mesajı veriyorsa sorun Ollama sunucusundadır: Ollama'yı sistem
tepsisinden kapatıp yeniden başlatın ve GPU belleği kullanan diğer uygulamaları kapatın.

## Bilinen eksikler

- Saldırı senaryoları, savunmalar, ASR ve savunma metrikleri yok.
- DOCX ve OCR (taranmış PDF) desteği yok; parola korumalı PDF açılmaz.
- OpenAI / Anthropic / DeepSeek adaptörleri gerçek API ile doğrulanmadı; maliyet tahmini yok.
- Aynı belge için tekrarlı çalıştırma (çoklu örnekleme) ve istatistiksel testler yok.
- Çalıştırma kesilirse `runs.jsonl` satırı yazılmaz (CSV satırları korunur).
- Veri kümesi yalnızca 5 sentetik belgedir; sonuçlar genellenemez.
