# M2Manager

Aplikacja webowa (PWA) do zarządzania dwoma mieszkaniami: faktury i wydatki, powierzchnie
pomieszczeń z edytorem rzutu oraz lista rzeczy do zakupu. Jedno wspólne konto dla dwóch osób.

**Stack:** ASP.NET Core Minimal API (.NET 10) + Blazor WebAssembly (ASP.NET Core Hosted) +
PostgreSQL (Neon) + Cloudflare R2 + Claude API + QuestPDF + ClosedXML. Hosting: Render (Docker).

---

## Spis treści

1. [Co potrafi aplikacja](#co-potrafi-aplikacja)
2. [Struktura projektu](#struktura-projektu)
3. [Uruchomienie lokalne](#uruchomienie-lokalne)
4. [Krok 1 — darmowa baza na Neon](#krok-1--darmowa-baza-na-neon)
5. [Krok 2 — bucket na Cloudflare R2](#krok-2--bucket-na-cloudflare-r2)
6. [Krok 3 — klucz API Anthropic](#krok-3--klucz-api-anthropic)
7. [Krok 4 — deploy na Render](#krok-4--deploy-na-render)
8. [Krok 5 — dodanie do ekranu początkowego na iPhone](#krok-5--dodanie-do-ekranu-początkowego-na-iphone)
9. [Koszty](#koszty)
10. [Konfiguracja — wszystkie zmienne](#konfiguracja--wszystkie-zmienne)
11. [Import istniejącego arkusza](#import-istniejącego-arkusza)
12. [Jak liczone są powierzchnie](#jak-liczone-są-powierzchnie)
13. [Testy](#testy)
14. [Rozwiązywanie problemów](#rozwiązywanie-problemów)

---

## Co potrafi aplikacja

**Moduł 1 — faktury i wydatki**
- Zdjęcie faktury prosto z aparatu telefonu (`capture="environment"`).
- Automatyczny odczyt danych przez Claude (sprzedawca, kwota brutto, data, sugerowana kategoria)
  — zawsze jako **propozycja do potwierdzenia**, nigdy jako prawda ostateczna.
- Zdjęcia w prywatnym buckecie R2, podgląd przez presigned URL o ograniczonej ważności.
- Filtry (mieszkanie, pomieszczenie, kategoria, zakres dat), edycja, usuwanie.
- Raporty: suma, rozbicie po kategoriach i miesiącach, eksport PDF i Excel.

**Moduł 2 — mieszkania, pomieszczenia i rzut**
- Pełny CRUD mieszkań i pomieszczeń, okna i drzwi jako osobne obiekty.
- Automatyczne liczenie obwodu, ścian brutto/netto, sufitów i sum dla całego mieszkania.
- Edytor rzutu w SVG: siatka ze snapem 10 cm, przeciąganie, zmiana rozmiaru,
  kliknięcie ściany dodaje okno albo drzwi, panel z wyliczeniami na żywo.
- Kalkulator materiałów (powierzchnia × warstwy ÷ wydajność) z dodaniem wyniku
  jednym kliknięciem na listę zakupów.

**Moduł 3 — lista rzeczy do zakupu**
- Kolumny 1:1 z prowadzonego wcześniej arkusza + status, priorytet, jednostka,
  rzeczywisty koszt, data zakupu, powiązanie z fakturą, „kto kupuje”.
- Domyślnie grupowanie po pomieszczeniach z podsumowaniem w nagłówku grupy,
  sortowanie po dowolnej kolumnie, filtry i wyszukiwarka.
- Edycja inline w tabeli oraz pełny formularz w panelu bocznym.
- Import istniejącego `.xlsx`, eksport do Excela i PDF, pasek postępu remontu.

---

## Struktura projektu

```
M2Manager/
├── M2Manager.Api/        # Minimal API + hosting Blazora + EF Core + serwisy (R2, OCR, eksporty)
│   ├── Configuration/    # opcje, parsowanie connection stringa
│   ├── Data/             # encje, DbContext, migracje, seeder
│   ├── Endpoints/        # auth, properties, invoices, shopping, reports
│   └── Services/         # storage, OCR, import xlsx, PDF/Excel
├── M2Manager.Client/     # Blazor WebAssembly (PWA)
│   ├── Components/       # formularz faktury, kalkulator materiałów, komunikaty
│   ├── Pages/            # logowanie, pulpit, faktury, mieszkania, rzut, zakupy, raporty
│   └── Services/         # ApiClient, AppState, stan logowania, formatowanie
├── M2Manager.Shared/     # DTO, enumy, AreaCalculator, MaterialCalculator (używane po obu stronach)
├── M2Manager.Tests/      # testy jednostkowe (xUnit)
├── Dockerfile
└── .env.example
```

`AreaCalculator` celowo leży w `Shared` — dzięki temu edytor rzutu liczy powierzchnie
w przeglądarce **tym samym kodem**, którego używa serwer. Nie ma szans na rozjazd wyników.

---

## Uruchomienie lokalne

**Wymagania:** .NET SDK 10.0 oraz dostęp do PostgreSQL (lokalnie albo darmowa baza na Neon).

```bash
git clone <adres-repo> && cd M2Manager
```

Ustaw sekrety (nie trafiają do repozytorium):

```bash
dotnet user-secrets --project M2Manager.Api set "ConnectionStrings:DefaultConnection" "postgresql://user:haslo@host/neondb?sslmode=require"
```

Wygeneruj hash hasła do wspólnego konta:

```bash
dotnet run --project M2Manager.Api -- hash-password "twoje-haslo"
```

Wynik (`pbkdf2$210000$...`) zapisz w konfiguracji:

```bash
dotnet user-secrets --project M2Manager.Api set "Auth:PasswordHash" "pbkdf2$210000$..."
```

Uruchom aplikację:

```bash
dotnet run --project M2Manager.Api
```

Aplikacja stoi pod adresem wypisanym w konsoli (domyślnie `http://localhost:5xxx`).
Migracje EF i dane startowe (dwa mieszkania, kategorie, przykładowe pomieszczenia)
wykonują się automatycznie przy pierwszym starcie.

> Bez skonfigurowanego R2 zdjęcia zapisują się na dysk lokalny (`App_Data/uploads`) —
> wygodne przy testach, nieprzydatne na Renderze, gdzie dysk kontenera znika po restarcie.
> Bez klucza Anthropic upload działa, tylko bez automatycznego odczytu.

---

## Krok 1 — darmowa baza na Neon

1. Załóż konto na <https://neon.tech> (plan **Free**, karta niepotrzebna).
2. **Create project** → wybierz region **EU (Frankfurt)** — najbliżej Polski.
3. Po utworzeniu projektu Neon pokazuje panel **Connection string**.
   Wybierz z listy bazę `neondb` i skopiuj gotowy ciąg — wygląda tak:

   ```
   postgresql://neondb_owner:npg_XXXX@ep-cool-name-a1b2c3.eu-central-1.aws.neon.tech/neondb?sslmode=require
   ```

4. Ten ciąg wklejasz do zmiennej `ConnectionStrings__DefaultConnection`.
   Aplikacja sama przetłumaczy format URI na ten, którego oczekuje Npgsql,
   i pominie parametry specyficzne dla Neona (np. `channel_binding`).

> Darmowy plan Neon usypia bazę po okresie bezczynności. Pierwsze zapytanie po przerwie
> może trwać kilka sekund — połączenie ma włączone ponawianie (`EnableRetryOnFailure`).

---

## Krok 2 — bucket na Cloudflare R2

1. Załóż konto na <https://dash.cloudflare.com> i wejdź w **R2 Object Storage**.
   Włączenie R2 wymaga podania karty, ale plan darmowy obejmuje 10 GB
   i nie nalicza opłat za transfer wychodzący.
2. **Create bucket** → nazwa np. `m2manager-faktury`, lokalizacja **EU**.
   Bucket zostaw **prywatny** (bez public access) — aplikacja generuje presigned URL-e.
3. Zapisz **Account ID** — widnieje w prawej kolumnie panelu R2.
4. **Manage R2 API Tokens** → **Create API token**:
   - uprawnienie: **Object Read & Write**,
   - zakres: tylko ten jeden bucket.
5. Skopiuj **Access Key ID** i **Secret Access Key** (sekret pokazuje się **tylko raz**).
6. Uzupełnij zmienne: `R2__AccountId`, `R2__AccessKeyId`, `R2__SecretAccessKey`, `R2__BucketName`.

---

## Krok 3 — klucz API Anthropic

1. Wejdź na <https://console.anthropic.com> i załóż konto.
2. **Settings → API Keys → Create Key**, skopiuj klucz (`sk-ant-...`).
3. Doładuj konto — odczyt faktur jest rozliczany za zużycie i przy kilkudziesięciu
   dokumentach miesięcznie to koszt rzędu pojedynczych złotówek.
4. Ustaw `Anthropic__ApiKey`. Model zostaw domyślny (`claude-sonnet-5`) —
   ma obsługę wizji i dobrze radzi sobie z polskimi paragonami.

---

## Krok 4 — deploy na Render

1. Wypchnij repozytorium na GitHuba.
2. Na <https://render.com> → **New → Web Service** → podłącz repo.
3. Ustawienia:
   - **Language / Runtime:** `Docker`
   - **Dockerfile Path:** `./Dockerfile`
   - **Region:** Frankfurt (ten sam co baza)
   - **Instance Type:** `Free` (albo `Starter` ~7 USD/mies., żeby usługa nie zasypiała)
4. W sekcji **Environment** dodaj zmienne (pełna lista w `.env.example`):

   | Zmienna | Wartość |
   |---|---|
   | `ConnectionStrings__DefaultConnection` | connection string z Neon |
   | `Auth__Username` | np. `dom` |
   | `Auth__PasswordHash` | wynik `hash-password` |
   | `R2__AccountId` | Account ID z Cloudflare |
   | `R2__AccessKeyId` | Access Key ID tokenu R2 |
   | `R2__SecretAccessKey` | Secret Access Key tokenu R2 |
   | `R2__BucketName` | np. `m2manager-faktury` |
   | `Anthropic__ApiKey` | klucz `sk-ant-...` |

   Portu **nie ustawiasz** — Render podaje go w zmiennej `PORT`, a aplikacja sama go odczytuje.

5. **Create Web Service**. Pierwszy build trwa kilka minut (kompilacja Blazora do WebAssembly).
6. Po wdrożeniu sprawdź `https://twoja-nazwa.onrender.com/api/health` — powinno zwrócić `{"status":"ok"}`.
7. Wejdź na adres główny i zaloguj się danymi ze wspólnego konta.

> **Darmowy plan Render** usypia usługę po 15 minutach bezczynności; pierwsze wejście
> po przerwie trwa ok. 30–60 sekund. Plan Starter (~7 USD/mies.) to eliminuje.

---

## Krok 5 — dodanie do ekranu początkowego na iPhone

1. Otwórz adres aplikacji w **Safari** (musi być Safari — Chrome na iOS nie instaluje PWA).
2. Zaloguj się.
3. Dotknij ikony **Udostępnij** (kwadrat ze strzałką w górę) na dolnym pasku.
4. Przewiń listę i wybierz **Dodaj do ekranu początkowego**.
5. Nazwa („Mieszkania”) podpowie się sama — potwierdź **Dodaj**.

Aplikacja uruchomi się w trybie pełnoekranowym, bez paska adresu.
Przycisk „Zrób zdjęcie aparatem” na stronie dodawania faktury otwiera od razu tylny aparat.

> Sesja trzyma się w cookie HttpOnly ważnym 30 dni, więc logowanie po każdym wejściu nie jest potrzebne.

---

## Koszty

| Usługa | Plan | Koszt |
|---|---|---|
| Neon (PostgreSQL) | Free | 0 zł |
| Cloudflare R2 | Free (10 GB) | 0 zł |
| Render | Free | 0 zł (usługa zasypia) |
| Render | Starter | ok. 7 USD/mies. (opcjonalnie) |
| Anthropic API | pay-as-you-go | kilka groszy za odczytaną fakturę |

Start kosztuje **0 zł**. Jedyny realny wydatek to odczyt faktur przez AI,
a aplikacja działa też bez niego (dane wpisuje się wtedy ręcznie).

---

## Konfiguracja — wszystkie zmienne

Pełny opis z komentarzami: [`.env.example`](.env.example) oraz
[`M2Manager.Api/appsettings.Example.json`](M2Manager.Api/appsettings.Example.json).

Zagnieżdżone klucze w zmiennych środowiskowych zapisujemy podwójnym podkreśleniem:
`Auth:PasswordHash` → `Auth__PasswordHash`.

Nic wrażliwego nie jest zapisane w kodzie — `appsettings.json` zawiera wyłącznie puste placeholdery.

---

## Import istniejącego arkusza

**Lista zakupów → Import / eksport → wybierz plik `.xlsx`.**

Importer rozpoznaje nagłówki bez względu na wielkość liter, polskie znaki i interpunkcję,
więc `~Koszt szt.`, `Koszt szt` i `koszt sztuki` trafią w tę samą kolumnę. Obsługiwane nagłówki:

`L.p` · `Pomieszczenie` · `Kategoria` · `Pozycja` · `Opis` · `Uwagi/obliczenia` · `Ilość` ·
`Jednostka` · `~Koszt szt.` · `~Koszt całk.` · `Planowany budżet (z amortyzacją)` ·
`Rzeczywisty koszt` · `Wykonawca/sklep` · `Link` · `Status` · `Priorytet` · `Data zakupu` · `Kto kupuje`

Zasady:
- wiersz nagłówków jest wyszukiwany w pierwszych 15 wierszach arkusza (tytuły nad tabelą nie przeszkadzają),
- brakujące pomieszczenia i kategorie zakładają się automatycznie,
- `Całe mieszkanie` w kolumnie *Pomieszczenie* oznacza brak przypisania do pokoju,
- pusty `~Koszt całk.` jest wyliczany jako `Ilość × ~Koszt szt.`,
- bez kolumny *Priorytet* pozycje ze znakiem zapytania w nazwie (np. „Wieszak na ręczniki?”)
  dostają priorytet *Fajnie by było*,
- wiersze bez nazwy pozycji są pomijane.

Import **dokłada** pozycje do wybranego mieszkania — nie kasuje tego, co już jest.

---

## Jak liczone są powierzchnie

```
obwód         = 2 × (długość + szerokość)      … albo z geometrii rzutu, gdy brak wymiarów
ściany brutto = obwód × wysokość               … wysokość pomieszczenia lub domyślna mieszkania
otwory        = Σ (szer_cm/100 × wys_cm/100 × sztuk)   dla otworów oznaczonych do odjęcia
ściany netto  = ręczne nadpisanie ?? (brutto − otwory − ściany wyłączone)
sufit         = ręczne nadpisanie ?? metraż podłogi
```

Do sum mieszkania wchodzą wyłącznie pomieszczenia z zaznaczonym *Wliczaj do sum*
(dzięki temu ogródek nie zawyża metrażu do malowania).

Przykład referencyjny — sypialnia 3,72 × 2,59 m, wysokość 2,60 m, drzwi 90×200 cm, okno 60×90 cm:

```
2 × (3,72 + 2,59) × 2,60 − 0,90 × 2,00 − 0,60 × 0,90
= 32,81 − 1,80 − 0,54
= 30,47 m²
```

Ten przypadek jest zapięty testem jednostkowym (`AreaCalculatorTests`) i wchodzi w skład danych startowych.

---

## Testy

```bash
dotnet test
```

Pokrycie: wyliczenia powierzchni (wymiary, geometria prostokątna i wielokątna, nadpisania ręczne,
otwory, sumy mieszkania), kalkulator materiałów, parser odpowiedzi AI (JSON w bloku markdown,
kwoty „1 234,56 zł”, różne formaty dat), sumy listy zakupów, sortowanie, rozpoznawanie nagłówków
arkusza, hashowanie haseł i parsowanie connection stringa z Neona.

---

## Rozwiązywanie problemów

**„Brak połączenia z bazą” przy starcie**
Nie ustawiono `ConnectionStrings__DefaultConnection` ani `DATABASE_URL`.

**Aplikacja startuje, ale każda strona z danymi pokazuje błąd**
Migracje nie przeszły — zajrzyj do logów. Najczęściej zły connection string albo uśpiona baza Neon.

**Odczyt AI zawsze kończy się „Odczyt nieudany”**
Brak `Anthropic__ApiKey` albo zdjęcie jest w formacie HEIC. Messages API przyjmuje JPEG, PNG, GIF,
WebP i PDF — w ustawieniach iPhone'a wybierz *Aparat → Formaty → Najbardziej zgodny*.

**Zdjęcia znikają po restarcie na Renderze**
R2 nie jest skonfigurowane i aplikacja zapisuje pliki na ulotnym dysku kontenera.
Uzupełnij zmienne `R2__*`.

**PDF-y nie generują się na Linuksie**
Brakuje `libfontconfig1` — `Dockerfile` już go instaluje. Przy własnym obrazie dodaj tę bibliotekę.

**Polskie znaki wychodzą jako krzaczki w eksportach**
Obraz działa bez ICU. Nie ustawiaj `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`
i nie używaj wariantów `alpine` bez ICU.
