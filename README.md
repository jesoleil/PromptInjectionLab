# PromptInjectionLab

Bağımsız bir .NET 8 araştırma prototipi. Amaç: **sabit kategorilerle çalışan LLM
tabanlı belge sınıflandırmasında** deneyler çalıştırmak ve sonuçları
tekrarlanabilir biçimde kaydetmek. Bu depo **Faz 0 + Faz 1** kapsamını içerir:
temiz TXT belgelerinin sınıflandırılması, CSV kaydı ve gecikme ölçümü.

> Henüz **yok**: saldırı senaryoları, savunmalar, PDF/DOCX desteği. Bunlar sonraki
> fazlarda eklenecek. Hiçbir performans/güvenlik başarısı iddia edilmemiştir.

## Çözüm yapısı

```
PromptInjectionLab.sln
nuget.config                     # paket kaynakları temiz (bkz. "Bağımlılıklar")
src/PromptInjectionLab.Core/     # alan modeli, pipeline, LLM istemcisi, metrikler
src/PromptInjectionLab.Cli/      # komutlar: check | run | metrics
tests/PromptInjectionLab.Tests/  # sıfır-bağımlılıklı test koşucusu
data/                            # örnek temiz veri kümesi (5 TXT + manifest.csv)
```

## Gereksinimler

- .NET 8 SDK
- [Ollama](https://ollama.com) (yerel LLM için) — kurulum aşağıda.

## Derleme ve test

```bash
dotnet build -c Release
dotnet run --project tests/PromptInjectionLab.Tests -c Release   # 40 test, ağ/Ollama gerektirmez
```

## Kullanım

Yapılandırma `src/PromptInjectionLab.Cli/appsettings.json` içindedir
(gizli değer yok). Yerel gizli değer/anahtar gerekiyorsa `appsettings.Local.json`
(git-ignore) veya ortam değişkeni (`Ollama__Model=...`) kullanın.

```bash
cd src/PromptInjectionLab.Cli

# 1) Ollama erişilebilir mi ve model yüklü mü?
dotnet run -c Release -- check

# 2) Temiz TXT belgelerini sınıflandır (Baseline)
dotnet run -c Release -- run --data ../../data/manifest.csv --out results.csv

# 3) Metrikleri hesapla
dotnet run -c Release -- metrics --in results.csv
```

### manifest.csv biçimi

```
item_id,path,true_category,variant,attack_template_id,target_category
doc1,clean/doc1.txt,Legal,clean,,
```
`path`, manifest dosyasının bulunduğu klasöre görelidir. `true_category` (ve ileride
kullanılacak `target_category`) izin verilen beş kategori adından biri olmalıdır
(büyük/küçük harf duyarsız); bilinmeyen bir ad, satır numarasıyla birlikte hata verir.

Örnek veri kümesi etiketleri: doc1=Legal, doc2=Finance, doc3=Technical,
doc4=General, doc5=HR.

> **Not (doc4):** `doc4.txt` bir pazarlama/kampanya metnidir. Kategori setinde
> ayrı bir pazarlama sınıfı olmadığı için bu belge **bilinçli bir deney etiketi
> olarak `General`** ile işaretlenmiştir; `General`, diğer dört sınıfa girmeyen
> (pazarlama dahil) metinleri kapsar. Bu bir modelin tahmini değil, araştırmacının
> belirlediği gerçek (ground-truth) etikettir.

### Sonuç CSV sütunları

Başlık satırı `RunRecord` özellik adlarından üretilir (sırası CSV sütun sırasıdır):

`RunId, TimestampUtc, PrototypeVersion, ItemId, FilePath, ContentSha256,
Variant, ExperimentCondition, AttackTemplateId, TargetCategoryId,
TrueCategoryId, TrueCategory, ModelName, Temperature, MaxTokens, Seed,
SystemInstructionVariant, PromptTemplateVersion, PromptSha256,
OllamaEndpoint, RawModelOutput, PredictedCategoryId, PredictedCategory,
ParseStatus, IsCorrect, LatencyMs, ErrorMessage`

- `TrueCategory` / `PredictedCategory`: okunabilir kategori adları.
  `*CategoryId` sütunları yalnızca iç (metrik) kimlikleridir.
- `LatencyMs`: belge okuma + prompt + model çağrısı + ayrıştırma toplam süresi (ms).
  İlk satır, modelin belleğe yüklenme süresini de içerebilir.
- `FilePath`: belgenin çalıştırıldığı makinedeki tam yolu.

Model çıktısı yalnızca izin verilen bir kategori adıyla **tam** eşleşirse `Valid`
olur (büyük/küçük harf, baştaki/sondaki boşluk ve sondaki `. , ; : ! ?`
tolere edilir). Listede olmayan tek kelime → `OutOfRange`; cümle vb. → `Unparseable`.
Geçersiz/hatalı sonuçlar (`Empty`, `Unparseable`, `OutOfRange`, `ApiError`,
`Timeout`, `ExtractionError`) **asla** doğru sayılmaz; `PredictedCategory` boş
kalır ve ham çıktı her zaman `RawModelOutput` içinde saklanır.

## Sabit kategoriler (varsayılan, değiştirilebilir)

`CategoryRegistry.Default`: `HR`, `Finance`, `Technical`, `Legal`, `General`
(iç kimlikler sırasıyla 1–5). Modele yalnızca adlar gösterilir ve yalnızca bir
ad döndürmesi istenir (prompt şablonu sürümü: `v2`).

## Ollama bağlantısı

İstemci Ollama'nın yerel `POST /api/chat` uç noktasını kullanır:
`stream: false`, `think: false` (thinking modellerinde akıl yürütme aşamasını
kapatır), `options: { temperature, num_predict, seed }`. Yanıt `message.content`
alanından okunur. `check` komutu `GET /api/tags` ile modelin yüklü olduğunu doğrular.
Varsayılan model: `qwen3.5:4b`.

## Bağımlılıklar

Bu prototip **harici NuGet paketi kullanmaz**. `Microsoft.Extensions.*`
(DI, Hosting, Options, Http, Logging, Configuration) kurulu
**Microsoft.AspNetCore.App** paylaşımlı çatısından `FrameworkReference` ile gelir.
CSV işlemleri dahili `Csv` sınıfıyla yapılır. Bu nedenle `nuget.config` paket
kaynaklarını temizler ve çözüm tamamen çevrimdışı derlenir. İnternet erişimi olan
bir makinede sonraki fazlarda (xUnit, PdfPig, DocumentFormat.OpenXml) paket
eklemek isterseniz `nuget.config` dosyasını silmeniz yeterlidir.

## Ollama kurulumu (Windows)

1. https://ollama.com/download adresinden Windows yükleyicisini indirip kurun.
2. Kurulumdan sonra Ollama arka planda `http://localhost:11434` üzerinde çalışır.
   Elle başlatmak için: `ollama serve`
3. Modeli indirin:
   ```
   ollama pull qwen3.5:4b
   ```
4. Doğrulayın:
   ```
   ollama list
   ```
5. Prototipte `check` komutu modelin hazır olduğunu teyit eder.
