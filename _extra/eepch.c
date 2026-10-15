#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define NLOC 25
#define NITEM 22
#define NDIR 6
#define SCREEN_COLS 40
#define TAB_WIDTH 10
#define INPUT_MAX 512
#define CARRIED 255
#define NOWHERE 0
#define EXIT_BAR 149
#define EXIT_GATE 152
#define START_MONEY 50000

typedef void (*cmd_fn)(void);

struct command {
    const char *verb;
    cmd_fn fn;
    const char *arg;
};

static void cmd_look(void);
static void cmd_quit(void);
static void cmd_inv(void);
static void cmd_examine(void);
static void cmd_help(void);
static void cmd_words(void);
static void cmd_pictures(void);
static void cmd_text(void);
static void cmd_move(void);
static void cmd_take(void);
static void cmd_read(void);
static void cmd_drop(void);
static void cmd_drink(void);
static void cmd_kill(void);
static void cmd_fish(void);
static void cmd_open(void);
static void cmd_close(void);
static void cmd_give(void);
static void cmd_poop(void);
static void cmd_wipe(void);
static void cmd_sit(void);
static void cmd_stand(void);
static void cmd_buy(void);
static void cmd_chop(void);
static void cmd_craft(void);
static void cmd_power(void);
static void cmd_feed(void);
static void cmd_dig(void);
static void cmd_eat(void);

static const char *const loc_text[NLOC + 1][12] = {
    {"Dawno temu zył sobie w swoim zamku nie- jaki książe Bolo. Spokojny był to wład-", "ca ; poddanych trzymał krótko za mordy  a i żonie jego nie było lepiej.", "Pewnego razu do Księcia przybyła wiedź- ma. \"Spełnie twoje każde życzenie jeśli", "tylko dostarczysz mi pięć gówien        eepcha!\" - powiedziała i zniknęła.", "Zafrasował sie księże Bolo. Eepcha      widział ostatnio w wieku 5 lat (teraz", "miał 60) w zoo. Ale - od czego byli     jego poddani ??", "I tu zaczyna Twoja misja ...            W razie kłopotow użyj komendy \"pomocy\"", "lub \"słówka\". Pisz je koniecznie małymi literami ; zwracając uwagę na polskie", "litery (np. cntl+a=ą). Powodzenia!!", NULL},
    {"Stoisz pod olbrzymim drzewem. Słyszysz  pięknie spiewające ptaszki i szum", "strumyka. Dookoła ciebie jest olbrzymia;bezkresna łąka . Stoisz na ścieżce pro-", "wadzącej z północy na południe.", NULL},
    {"Stoisz przed ogromną skałą. Daleko na   południu widzisz drzewo ; do którego", "prowadzi ścieżka. W drugą strone ścież- ka idzie na zachód. Na północy widzisz", "wejście do ciemnej jaskini. Dobiegają   z niej dziwne dźwieki.", NULL},
    {"Jesteś w ciemnej jaskini. W zasadzie nicnie widzisz ; ale ktoś tu chyba z tobą  jest ...", NULL},
    {"Stoisz pod ogromną skałą. Na zachodzie  widzisz ciemną jaskinię ; prowadzi do", "niej ścieżka. W drugą stronę możesz     dojść do skrzyżowania.", "Do skały przymocowana jest tabliczka.", NULL},
    {"Stoisz na skrzyżowaniu dróg. Na południuścieżka pnie się na ogromną skałę ;", "na północy - schodzi w kierunku morza   (słyszysz jego szum). Daleko na zacho-", "dzie widać zamek księcia Bolka ; na     wschodzie jest coś ; co stąd wygląda", "jak jaskinia.", NULL},
    {"Siedzisz na grubym konarze stuletniego  dębu. Gęste liście zasłaniają ci drogę", "ale masz stąd doskonały widok na okoli- cę. Na północy widzisz samotną skałę.", NULL},
    {"Stoisz na ścieżce prowadzącej ze wscho- du na zachód. Na północy ; widzisz zam-", "mek księcia Bolka ; otoczony fosą (wyg- ląda na dosyć głęboką). Prowadzi do", "niego droga przez most. Daleko na       wschodzie widzisz samotną skałę ;", "na zachodzie ciągnie się gęsty las.", NULL},
    {"Stoisz na moście .Prowadzi przez niego  droga do zamku księcia Bolka. Dokładnie", "pod sobą widzisz głęboką fosę.", NULL},
    {"Stoisz pod bramą zamku księcia Bolka.   Wygląda na to ; że nie ma tu żywej du-", "szy. Na wschodzie widzisz samotną skałę;a na południu - drogę wschód-zachód.", "Na murze zamku przybita jest tabliczka.", NULL},
    {"Wyszedłeś nad morze. Stopy grzęzną ci   w piasku ; a szum morza i pisk mew", "działa kojąco na twoje nerwy.", NULL},
    {"Stoisz na samym czubku skały. Daleko na zachodzie widzisz ponure mury zamku", "księcia Bolka ; na północy jest chyba   morze ; widzisz mewy nad wzgórzami.", NULL},
    {"Jesteś w gęstym i ciemnym lesie. Panuje tu prawie noc ; gałęzie drzew zasłania-", "ją dostęp światła. Słyszysz tajemnicze  pojękiwania i ryki. Cały czas dookoła", "coś się rusza.", NULL},
    {"Stoisz nad rzeczką na skraju lasu. Dróżka rozdziela się tu na dwie .", "Na północy ciągnie się wzdłuż rzeczki ; na południu - dochodzi do mostku.", NULL},
    {"Stoisz na mostku nad rzeczką. Mostek    jest nieco zdezelowany i lepiej nie", "opierać się o poręcze. Na zachodzie jestgęsty las ; na wschodzie zaś - widać", "jakieś gospodarstwo.", NULL},
    {"Stoisz na cudzym podwórku. Na północy   widzisz sławojkę ; na południu zaś", "stoi obora. Ktoś tu jest.", NULL},
    {"Idziesz sobie drogą. Na północy widać   olbrzymi ; stary dąb. Na zachodzie stoi", "jakiś budynek ; z południa dobiega do   ciebie szum morza.", NULL},
    {"Jesteś w sławojce. Z dziury wydobywa    sią smród i straszliwa ilość Much.", "Możesz sobie usiąść wygodnie i bezpiecz-nie.", NULL},
    {"Stoisz pod jakimś murem. Na północ pro- wadzi ścieżka do gospodarstwa.", "W murze jest furtka ; zamknięta na zamkitypu \"Skarbiec\" ; \"Tytan\" i \"Yale\".", "Do muru przymocowana jest tabliczka.", NULL},
    {"Jesteś nad brzegiem morza. Picachu jest tu sporo ; wody jeszcze więcej. Plaża", "ciągnie się na zachód . Na wschodzie    brzeg staje się nieco skalisty.", NULL},
    {"Stoisz nad brzegiem uroczej zatoczki    morskiej. Stopy omywa ci ciepłe morze;", "twarz chłosta wietrzyk. Daleko na zacho-dzie widać ujście małej rzeczki.", NULL},
    {"Stoisz na drodze wschód-zachód. Przed   tobą jest bar \"Rybacki\". Jest otwarty ;", "a z wnętrza dobywa się hałas pijackich  głosów.", NULL},
    {"Stoisz przed ladą w barze \"Rybackim\".   Na ścianie wisi cennik. Dookoła jest", "pełno zalanych miejscowych żuli.", NULL},
    {"Jesteś w dużym pokoju. Na podłodze leży piękny ; zdobiony dywan. Pod ścianą", "stoi regał ; stół i szafa. Telewizor na regale jest wyłączony. Na ścianie wi-", "dzisz wyłącznik podpisany \"Ogrodzenie\". W pokoju jest gospodarz.", NULL},
    {"Stoisz przed dużą klatką. W środku jest jeden Eepch. Na północ prowadzi ścieżka", "do furtki w murze (który otacza całą    klatkę). Na zachodzie widać małą", "komórkę.", NULL},
    {"Jesteś w komórce z żarciem dla Eepcha.  Jest tu cała masa słoików z Eepch's", "Food - czymś ; czego się już od lat nie produkowało.", NULL},
};

static const unsigned char exits_init[NDIR][NLOC + 6] = {
    {0, 2, 3, 0, 0, 11, 0, 8, 9, 0, 5, 0, 0, 0, 13, 17, 1, 0, 15, 16, 0, 22, 0, 0, 18, 0, 0, 0, 0, 0, 0},
    {0, 16, 1, 2, 0, 10, 0, 0, 7, 8, 0, 5, 0, 14, 0, 18, 19, 15, 152, 0, 0, 0, 149, 0, 0, 0, 0, 0, 0, 0, 0},
    {0, 0, 0, 0, 2, 4, 0, 5, 0, 0, 20, 0, 7, 12, 0, 14, 21, 0, 0, 0, 19, 0, 0, 15, 0, 24, 0, 0, 0, 0, 0},
    {0, 0, 4, 0, 5, 7, 0, 12, 0, 0, 0, 0, 13, 0, 15, 23, 0, 0, 0, 20, 10, 16, 0, 0, 25, 0, 0, 0, 0, 0, 0},
    {0, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
    {0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0},
};

static const unsigned char obj_init[NITEM + 1] = {0, 1, 22, 15, 0, 20, 0, 0, 0, 0, 0, 13, 0, 0, 0, 0, 17, 0, 0, 25, 0, 0, 23};

static const char *const item_name[NITEM + 1] = {"", "papier", "kluczyk", "łopatę", "gówno 1", "wędkę", "hamburgera", "piwo Leżajsk", "piwo Okocim", "piwo kuflowe", "żyłkę", "haczyk", "wędzisko", "gówno 2", "siekierę", "gówno 3", "srajtaśmę", "gówno 4", "brudną srajtaśmę", "żarcie", "gówno 5", "gówna", "książkę"};

static const char *const item_desc[NITEM + 1] = {NULL, "Jest coś na nim napisane", "Bardzo stary ; dziś już nikt takich nie używa.", "Nowiutka. Doskonała do kopania grobów.", "Nieco stare ; ale niewątpliwie zrobione przez Eepcha.", "Zwykła wędka bambusowa.", "Zwykły hamburger ; nawet bez sałaty.", "Puszkowane.", "Eksportowe.", "Sikacz ; dosyć mocno chrzczony.", "Długa na 150 m.", "Doskonały na rybki.", "Dla profesjonalnego rybaka!", "Niewątpliwie Eepcha.", "Długa (1 m.) i ciężka (5 kg.).", "Wygląda na gówienko Eepcha (dość małe).", "Uniwersalna bumaga (chroni ; ratuje ;   pomaga).", "Piękne brązowe gówienko!", "Sam ją uświniłeś.", "Eepch's Food- to było żarcie eksportowe!", "Ogromne ; świeże ; cuchnące gówno!", "Piękne okazy! W sam raz dla księcia!", "\"Hodowla i zastosowanie Eepchów\""};

static const char *const item_read[NITEM + 1] = {NULL, "\"Za znalezienie pięciu gówien eepcha -   nagroda $500.000.000\"                       Podpisano :Książe Bolo", NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, "Made by Celwiskoza Yelenia Goora.", NULL, NULL, "Produced by Eedeeotha Corp.", NULL, NULL, "\"... gówno ludzkie ma identyczne właści-wości jak gówno Eepcha ...\""};

static const char *const eat_msg[NITEM + 1] = {NULL, "Gut celuloza!", "Lekko niestrawna", "Lekko niestrawna", "Szkoda by go było!", "Za długa.", "Pycha !", "Tego się nie je! To się pije!", "Tego się nie je! To się pije!", "Tego się nie je! To się pije!", "Niezdrowa", "Przecież go potem nie wysrasz!", "Beznadziejny pomysł.", "Szkoda by go było!", "Za ostra jak dla ciebie", "Szkoda by było.", "Gut celuloza !", "Szkoda by go było!", "Taką obsraną ?", "Nie dla ciebie!", "Szkoda by go było!", "Wszystkie na raz ?", "Ciut niestrawna!"};
static const unsigned char eat_consumes[NITEM + 1] = {0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0};

static const char *const help_text[NLOC + 1] = {NULL, NULL, NULL, "Nic tu po tobie.", "Tu jest tak ciemno ; że i ja nic nie    widzę", "Spróbuj pohandlować.", NULL, NULL, "Lepiej nie kąpać się w fosie.", "Tu trzeba oddać wszystkie gówna.", "Woda jest słona.", "Uważaj ; żebyś nie spadł na mordę.", "Spływaj stąd lepiej...", "Wędka może służyć do różnych celów...", NULL, "Od domu do muru prowadzą druty.", NULL, "Lepiej tu nie srać...", "Drzwi mogą cię zabić!", "Piękną pogodę mamy dzisiaj!", NULL, "Uważaj na żuli!", "Rybak chce pić !", "Gospodarz nie pozwoli ci wyłączyć prądu.", "Eepch jest wygłodzony.", NULL};

struct scenery { const char *name; const char *text; int loc; };
static const struct scenery scenery_list[] = {
    {"drzewo", "100-letni dąb.Możesz się na niego       wspiąć.", 1},
    {"łąkę", "Zielona.", 1},
    {"skałę", "Chyba wapienna ; zbyt stroma by się na  nią wspinać z tej strony.", 2},
    {"wejście", "Nic nie widać.", 2},
    {"jaskinię", "Stąd ??", 2},
    {"dźwięki", "Przypominają rżenie konia.", 2},
    {"drogę", "Taka sobie; rozjeżdżona przez traktory.", 16},
    {"skałę", "Bardzo stroma.", 4},
    {"skrzyżowanie", "Jest tu drogowskaz.", 5},
    {"drogowskaz", "\"Do zamku Ks. Bolka\"", 5},
    {"morze", "Raczej słone i głębokie.", 10},
    {"piasek", "Jest żółty i sypki.", 10},
    {"czubek", "Srak tu naptał.", 11},
    {"most", "Częściowo zwodzony.Od dawna nikt go nie naprawiał.", 8},
    {"fosę", "Jest w niej wskaźnik - pokazuje 8.5 m.  głębokości.", 8},
    {"bramę", "Ciężka ; z żelaznymi okuciami. Na każ-  dym skrzydle jest kołatka ; a na górze  widać herb Ks. Bolka", 9},
    {"rzeczkę", "10 m. szerokości ; płynie dość leniwie.", 13},
    {"dróżkę", "Zwykła ; polna dróżka", 13},
    {"mostek", "Uważaj ; bo się może pod tobą rozpaść !", 14},
    {"poręcze", "Jak widzisz - prawie ich nie ma.", 14},
    {"podwórko", "Typowe wiejskie podwórko ; dosyć zad-   bane.", 15},
    {"sławojkę", "Typowa wiejska sławojka z otworkiem.", 15},
    {"oborę", "Połączona z domem", 15},
    {"dom", "Drewniany.", 15},
    {"sławojkę", "Dosyć wygodna ; aczkolwiek wygląda na   lekko przepełnioną. Cuchnie nieludzko.", 17},
    {"dziurę", "Na krawędzi są jakieś białe ślady.", 17},
    {"muchy", "Ten gatunek lubił Eepchy!!", 17},
    {"mur", "Betonowy.Na wierzchu jest drut kolczas- ty.", 18},
    {"furtkę", "żelazna - na oko 40 mm grubości.", 18},
    {"zamki", "Jesteś zbyt kiepskim włamywaczem by je  otworzyć.", 18},
    {"klamkę", "Dosyć dziwnej konstrukcji.", 18},
    {"druty", "Kolczasty - i chyba pod napięciem.", 18},
    {"pokój", "40 m powierzchni", 23},
    {"dywan", "Perski.", 23},
    {"regał", "Jest na nim telewizor i masa książek.", 23},
    {"stół", "Zwyczajny.Stoi przy nim fotel.", 23},
    {"szafa", "Zamknięta na klucz.", 23},
    {"bar", "Typowy bar III kategorii (masa piwa i   żuli).", 21},
    {"ladę", "Nieco zalana piwem.", 22},
    {"cennik", "Jest tu coś napisane.", 22},
    {"żuli", "Lepiej im się nie przyglądaj za mocno.", 22},
    {"żula", "Którego ?", 22},
    {"klatkę", "Mocna ; żelazna.", 24},
    {"Eepcha", "Bardzo wygłodzony.", 24},
    {"eepcha", "Bardzo wygłodzony.", 24},
    {"komórkę", "Drewniana.", 24},
    {"słoik", "Ale który ??", 25},
    {"słoiki", "W środku jest żarcie dla Eepcha !", 25},
    {"wyłącznik", "Do furtki.", 23},
    {NULL, NULL, 0}
};

static const struct command commands[] = {
    {"patrzę", cmd_look, NULL},
    {"quit", cmd_quit, NULL},
    {"koniec", cmd_quit, NULL},
    {"spis", cmd_inv, NULL},
    {"inv", cmd_inv, NULL},
    {"i", cmd_inv, NULL},
    {"badam", cmd_examine, NULL},
    {"oglądam", cmd_examine, NULL},
    {"help", cmd_help, NULL},
    {"pomocy", cmd_help, NULL},
    {"słówka", cmd_words, NULL},
    {"obrazki", cmd_pictures, NULL},
    {"tekst", cmd_text, NULL},
    {"text", cmd_text, NULL},
    {"t", cmd_text, NULL},
    {"o", cmd_pictures, NULL},
    {"północ", cmd_move, NULL},
    {"południe", cmd_move, NULL},
    {"wschód", cmd_move, NULL},
    {"zachód", cmd_move, NULL},
    {"góra", cmd_move, NULL},
    {"dół", cmd_move, NULL},
    {"pn", cmd_move, "północ"},
    {"pd", cmd_move, "południe"},
    {"w", cmd_move, "wschód"},
    {"z", cmd_move, "zachód"},
    {"g", cmd_move, "góra"},
    {"d", cmd_move, "dół"},
    {"sprawdzam", cmd_examine, NULL},
    {"biorę", cmd_take, NULL},
    {"zabieram", cmd_take, NULL},
    {"podnoszę", cmd_take, NULL},
    {"czytam", cmd_read, NULL},
    {"rzucam", cmd_drop, NULL},
    {"zostawiam", cmd_drop, NULL},
    {"opuszczam", cmd_drop, NULL},
    {"piję", cmd_drink, NULL},
    {"zabijam", cmd_kill, NULL},
    {"atakuję", cmd_kill, NULL},
    {"morduję", cmd_kill, NULL},
    {"łowię", cmd_fish, NULL},
    {"wyławiam", cmd_fish, NULL},
    {"otwieram", cmd_open, NULL},
    {"zamykam", cmd_close, NULL},
    {"daję", cmd_give, NULL},
    {"sram", cmd_poop, NULL},
    {"podcieram", cmd_wipe, NULL},
    {"siadam", cmd_sit, NULL},
    {"wstaję", cmd_stand, NULL},
    {"kupuję", cmd_buy, NULL},
    {"ścinam", cmd_chop, NULL},
    {"rąbię", cmd_chop, NULL},
    {"szykuję", cmd_craft, NULL},
    {"składam", cmd_craft, NULL},
    {"wyłączam", cmd_power, NULL},
    {"karmię", cmd_feed, NULL},
    {"kopię", cmd_dig, NULL},
    {"jem", cmd_eat, NULL},
    {NULL, NULL, NULL}
};

static const char *const dir_name[NDIR] = {"północ", "południe", "wschód", "zachód", "góra", "dół"};

static const struct {
    const char *name;
    int price;
    int offset;
} shop[] = {
    {"hamburgera", 10000, 0},
    {"piwo Leżajsk", 7500, 1},
    {"piwo Okocim", 6800, 2},
    {"piwo kuflowe", 4000, 3},
    {NULL, 0, 0}
};

static const unsigned char dung_items[5] = {4, 13, 15, 17, 20};

static unsigned char exits[NDIR][NLOC + 6];
static unsigned char obj[NITEM + 1];
static char o1[INPUT_MAX];
static char o2[INPUT_MAX];
static int loc, money, tekst, item;
static int zatoczka, rybak, sram, szkat, prad, gosp, drzewo, siad;
static int page_count, ended, out_col;

static void put_ch(int c)
{
    if (c == '\n') {
        putchar('\n');
        out_col = 0;
        return;
    }
    if ((c & 0xC0) != 0x80) {
        if (out_col == SCREEN_COLS) {
            putchar('\n');
            out_col = 0;
        }
        out_col++;
    }
    putchar(c);
}

static void put_str(const char *s)
{
    for (; *s != '\0'; s++)
        put_ch((unsigned char)*s);
}

static void put_line(const char *s)
{
    put_str(s);
    put_ch('\n');
}

static void put_num(int n)
{
    char buf[32];
    sprintf(buf, "%d", n);
    put_str(buf);
}

static void put_tab(void)
{
    int pad = TAB_WIDTH - out_col % TAB_WIDTH;
    while (pad-- > 0)
        put_ch(' ');
}

static int read_line(char *buf, size_t n)
{
    size_t len;
    int c;
    fflush(stdout);
    if (fgets(buf, (int)n, stdin) == NULL)
        return 0;
    len = strlen(buf);
    if (len > 0 && buf[len - 1] != '\n' && len == n - 1) {
        while ((c = getchar()) != EOF && c != '\n')
            ;
    }
    while (len > 0 && (buf[len - 1] == '\n' || buf[len - 1] == '\r'))
        buf[--len] = '\0';
    out_col = 0;
    return 1;
}

static int get_key(void)
{
    char buf[INPUT_MAX];
    if (!read_line(buf, sizeof buf))
        exit(0);
    return (unsigned char)buf[0];
}

static void wait_return(void)
{
    put_str("RETURN ");
    get_key();
}

static void put_text(const char *const *lines)
{
    int limit = tekst == 1 ? 4 : 12;
    for (; *lines != NULL; lines++) {
        page_count++;
        if (page_count == limit) {
            wait_return();
            page_count = 0;
        }
        put_line(*lines);
    }
}

static int item_find(const char *name)
{
    int i;
    for (i = 1; i <= NITEM; i++) {
        if (strcmp(item_name[i], name) == 0)
            return i;
    }
    return 0;
}

static int has_arg(void)
{
    return strcmp(o2, "*") != 0;
}

static int check(void)
{
    if (!has_arg()) {
        put_line("Ale co ??");
        return 0;
    }
    item = item_find(o2);
    if (item == 0) {
        put_line("Nie znam takiego przedmiotu.");
        return 0;
    }
    return 1;
}

static int check_carried(void)
{
    if (obj[item] != CARRIED) {
        put_line("Nie niesiesz tego przedmiotu!");
        return 0;
    }
    return 1;
}

static void die(void)
{
    put_line("Gra jest skonczona...Chyba nie żyjesz!");
    ended = 1;
}

static void win(void)
{
    put_line("Wygrałeś!!!!!!!!!!!");
    put_line("Ha!Ha!");
    ended = 1;
}

static void special(void)
{
    int i, all;
    if (loc == 4 && szkat != 0) {
        put_str("W wykopanym dołku leży ");
        if (szkat == 2)
            put_str("otwarta ");
        put_line("szkatuła.");
    }
    if (loc == 5 && rybak != 2) {
        put_line("Jest tu stary ; zalany rybak.");
        if (rybak == 0)
            put_line("Mówi ; że chciałby się czegoś napić.");
        if (rybak == 1)
            put_line("Mówi ; że poszedłby na rybki.");
    }
    if (loc == 13 && zatoczka == 0)
        put_line("W zatoczce po drugiej stronie rzeczki   pływa sobie gówno.");
    if (loc == 1 && drzewo == 1)
        put_line("Drzewo jest ścięte.");
    if (loc == 23 && gosp == 1)
        put_line("Gospodarz nie żyje.");
    all = 1;
    for (i = 0; i < 5; i++) {
        if (obj[dung_items[i]] != CARRIED)
            all = 0;
    }
    if (all) {
        for (i = 0; i < 5; i++)
            obj[dung_items[i]] = NOWHERE;
        obj[21] = CARRIED;
        put_line("Ha!");
        cmd_inv();
    }
    if (sram == 1)
        put_line("Swędzi cię dupa.");
}

static void location(void)
{
    int h, d, shown;
    page_count = 0;
    if (loc == 0) {
        put_text(loc_text[0]);
        loc = 1;
    }
    put_text(loc_text[loc]);
    special();
    put_str("Widzisz : ");
    shown = 0;
    for (h = 1; h <= NITEM; h++) {
        if (obj[h] == loc) {
            if (shown)
                put_str(",");
            put_str(item_name[h]);
            shown = 1;
        }
    }
    if (!shown)
        put_str("nic");
    put_ch('\n');
    put_str("Wyjscia : ");
    shown = 0;
    for (d = 0; d < NDIR; d++) {
        if (exits[d][loc] != 0) {
            if (shown)
                put_str(",");
            put_str(dir_name[d]);
            shown = 1;
        }
    }
    if (!shown)
        put_str("nie ma");
    put_ch('\n');
}

static int gcom(void)
{
    char in[INPUT_MAX];
    char *sp;
    for (;;) {
        put_ch('\n');
        put_str("Co robisz ? ");
        if (!read_line(in, sizeof in))
            return 0;
        if (in[0] != '\0')
            break;
    }
    sp = strchr(in, ' ');
    if (sp == NULL) {
        strcpy(o1, in);
        strcpy(o2, "*");
    } else {
        memcpy(o1, in, (size_t)(sp - in));
        o1[sp - in] = '\0';
        strcpy(o2, sp + 1);
    }
    return 1;
}

static void ecom(void)
{
    const struct command *c;
    for (c = commands; c->verb != NULL; c++) {
        if (strcmp(c->verb, o1) == 0) {
            if (c->arg != NULL)
                strcpy(o1, c->arg);
            c->fn();
            return;
        }
    }
    put_line("Nie znam takiego polecenia");
}

static void sign(void)
{
    if (loc == 4) {
        put_line(" Tu leży");
        put_line("  Johny Mc Vawakh (1921-1991)");
        put_line("Ostatni hodowca i obrońca Eepchów");
    }
    if (loc == 9) {
        put_line(" Zamek księcia Bolka Miękkiego");
        put_line("Pana Wałlandii i Wysp Jeulandzkich");
        put_line(" Nieupoważnionym wstęp wzbroniony.");
    }
    if (loc == 18)
        put_line(" Uwaga ! Klamka i druty pod napięciem!");
}

static void price_list(void)
{
    if (loc != 22) {
        put_line("Ale tu go nie ma !!");
        return;
    }
    put_line("Hamburger - 10000 zł.");
    put_line("Piwo Leżajsk - 7500 zł");
    put_line("Piwo Okocim - 6800 zł");
    put_line("Piwo kuflowe - 4000 zł");
}

static void cmd_look(void)
{
    location();
}

static void cmd_quit(void)
{
    put_line("Czy jesteś pewien (T/N) ?");
    if (get_key() != 'T')
        return;
    exit(0);
}

static void cmd_inv(void)
{
    int h, shown = 0;
    put_str("Niesiesz : ");
    for (h = 1; h <= NITEM; h++) {
        if (obj[h] == CARRIED) {
            if (shown)
                put_str(",");
            put_str(item_name[h]);
            shown = 1;
        }
    }
    if (!shown)
        put_str("nic");
    put_ch('\n');
}

static void cmd_help(void)
{
    if (help_text[loc] == NULL)
        put_line("Rusz wreszcie tym swoim łbem!!");
    else
        put_line(help_text[loc]);
}

static void cmd_words(void)
{
    const struct command *c;
    put_line("Słówka :");
    for (c = commands; c->verb != NULL; c++) {
        put_str(c->verb);
        put_tab();
    }
    put_ch('\n');
}

static void cmd_pictures(void)
{
    tekst = 1;
    put_line("o.k.");
}

static void cmd_text(void)
{
    tekst = 0;
    put_line("o.k.");
}

static void cmd_move(void)
{
    int d, t;
    if (siad == 1) {
        put_line("Chcesz chodzić na siedząco ??");
        return;
    }
    for (d = 0; d < NDIR; d++) {
        if (strcmp(o1, dir_name[d]) == 0)
            break;
    }
    if (d == NDIR)
        return;
    t = exits[d][loc];
    if (t == 0) {
        put_line("Nie możesz iść w tym kierunku !!");
        return;
    }
    for (;;) {
        if (t < 128) {
            loc = t;
            location();
            return;
        }
        if (t == EXIT_BAR) {
            if (obj[9] != CARRIED) {
                loc = 21;
                location();
                return;
            }
            put_line("Chcesz wyjść razem z kuflem ?");
            return;
        }
        if (t == EXIT_GATE) {
            if (prad == 1) {
                loc = 24;
                location();
                return;
            }
            put_line("Kopnął cię prąd .Jesteś nieżywy.");
            die();
            return;
        }
        t -= 128;
    }
}

static void cmd_take(void)
{
    if (strcmp(o2, "gówno") == 0) {
        put_line("Musisz jeszcze podać jego numer !");
        return;
    }
    if (!check())
        return;
    if (obj[item] != loc) {
        put_line("Nie ma tu tego przedmiotu!");
        return;
    }
    obj[item] = CARRIED;
    put_line("O.k.");
    cmd_inv();
}

static void cmd_examine(void)
{
    const struct scenery *s;
    if (!has_arg()) {
        put_line("Ale co ??");
        return;
    }
    if (strcmp(o2, "portfel") == 0) {
        put_str("Masz w nim ");
        put_num(money);
        put_line(" złotych.");
        return;
    }
    item = item_find(o2);
    if (item != 0) {
        if (!check_carried())
            return;
        put_line(item_desc[item]);
        return;
    }
    if (strcmp(o2, "tabliczkę") == 0) {
        sign();
        return;
    }
    for (s = scenery_list; s->name != NULL; s++) {
        if (strcmp(s->name, o2) == 0 && s->loc == loc) {
            put_line(s->text);
            return;
        }
    }
    put_line("Nie możesz tego obejrzeć!");
}

static void cmd_read(void)
{
    if (strcmp(o2, "tabliczkę") == 0) {
        sign();
        return;
    }
    if (strcmp(o2, "cennik") == 0) {
        price_list();
        return;
    }
    if (!check())
        return;
    if (item_read[item] != NULL)
        put_line(item_read[item]);
}

static void cmd_drop(void)
{
    if (strcmp(o2, "gówno") == 0) {
        put_line("Musisz jeszcze podać jego numer!");
        return;
    }
    if (!check() || !check_carried())
        return;
    obj[item] = (unsigned char)loc;
    cmd_inv();
}

static void cmd_drink(void)
{
    int i, any;
    if (strlen(o2) >= 4 && strncmp(o2, "piwo", 4) == 0) {
        any = 0;
        for (i = 6; i <= 9; i++) {
            if (obj[i] == CARRIED)
                any = 1;
        }
        if (!any) {
            put_line("Chyba śnisz.");
            return;
        }
        for (i = 6; i <= 9; i++)
            obj[i] = NOWHERE;
        put_line("O.k.");
        cmd_inv();
        return;
    }
    if (strcmp(o2, "wodę") == 0 || !has_arg()) {
        if (loc == 19 || loc == 20 || loc == 10)
            put_line("A fe ! Słona !");
        else if (loc == 14 || loc == 13)
            put_line("O.k.");
        else
            put_line("Tu nie ma żadnej wody!");
        return;
    }
    cmd_kill();
}

static void cmd_kill(void)
{
    if (strcmp(o2, "gospodarza") == 0) {
        if (loc != 23) {
            put_line("Nie ma go tu.");
            return;
        }
        if (gosp == 1) {
            put_line("Drugi raz ??");
            return;
        }
        gosp = 1;
        put_line("Gospodarz pada na mordę nieżywy.");
        return;
    }
    if (strcmp(o2, "rybaka") == 0) {
        if (loc != 5 || rybak == 2) {
            put_line("Ale go tu nie ma!");
            return;
        }
        put_line("Nic z tego ! Dostajesz tylko w ryło.");
        return;
    }
    if (loc == 3) {
        put_line("Niestety ,po ciemku nie widzisz,kogo    atakujesz. Dostajesz w łeb i kończy Ci  się wizja i fonia.");
        die();
        return;
    }
    if (strcmp(o2, "żula") == 0 || strcmp(o2, "żuli") == 0) {
        if (loc != 22) {
            put_line("Nie tutaj.");
            return;
        }
        put_line("Dostajesz w ryło tak potężnie,że lądu-  jesz pod drzwiami");
        return;
    }
    if (has_arg()) {
        put_line("To niemożliwe.");
        return;
    }
    put_line("Ale kogo ??");
}

static void cmd_fish(void)
{
    if (loc != 10 && loc != 19 && loc != 20 && loc != 13 && loc != 14 && loc != 8) {
        put_line("Idiota!To się robi nad wodą!");
        return;
    }
    if (strcmp(o2, "gówno") != 0 && has_arg()) {
        put_line("Nic z tego.");
        return;
    }
    if (obj[12] != CARRIED) {
        put_line("Ciekawe , czym !!");
        return;
    }
    if (loc == 13) {
        if (zatoczka == 1) {
            put_line("A po co ?");
            return;
        }
        obj[13] = 13;
        put_line("Wyciągnąłeś przepięgne gówno !");
        zatoczka = 1;
        return;
    }
    put_line("Kiepsko dziś biorą...");
}

static void cmd_open(void)
{
    if (!has_arg()) {
        put_line("Ale co ??");
        return;
    }
    if (strcmp(o2, "szkatułę") != 0) {
        put_line("Nic tu się nie da otworzyć.");
        return;
    }
    if (obj[2] != CARRIED) {
        put_line("Nie masz klucza !");
        return;
    }
    if (loc != 4 || szkat == 0) {
        put_line("Nic tu takiego nie ma.");
        return;
    }
    if (szkat == 2) {
        put_line("Ale ona jest już otwarta!");
        return;
    }
    if (szkat == 3) {
        put_line("A w środku nic nie ma ...");
        szkat = 2;
        return;
    }
    szkat = 2;
    put_line("Wieko odpadło z trzaskiem ... W środku  leży ... Gówno 1 !!!");
    obj[4] = 4;
}

static void cmd_close(void)
{
    if (!has_arg()) {
        put_line("Ale co ??");
        return;
    }
    if (strcmp(o2, "szkatułę") != 0) {
        put_line("Nic tu się nie da otworzyć.");
        return;
    }
    if (loc != 4 || szkat == 0) {
        put_line("Nic tu takiego nie ma.");
        return;
    }
    if (szkat == 1 || szkat == 3) {
        put_line("Ale ona jest zamknięta!");
        return;
    }
    put_line("O.K.");
    szkat = 3;
    if (obj[4] == 4) {
        szkat = 1;
        obj[4] = NOWHERE;
    }
}

static void cmd_give(void)
{
    if (loc == 5 && rybak == 0) {
        if (strlen(o2) >= 4 && strncmp(o2, "piwo", 4) != 0) {
            put_line("Chcesz w ryj ? Daj to , o co prosi!");
            return;
        }
        put_line("Rybak bierze całe twoje piwo.");
        obj[7] = NOWHERE;
        obj[8] = NOWHERE;
        rybak = 1;
        put_line("W zamian daje Ci żyłkę.");
        obj[10] = (unsigned char)loc;
        return;
    }
    if (loc == 5 && rybak == 1) {
        if (strcmp(o2, "wędzisko") != 0) {
            put_line("A po co mu to ?");
            return;
        }
        if (obj[12] != CARRIED) {
            put_line("Chcesz go oszukać ?");
            return;
        }
        obj[12] = NOWHERE;
        rybak = 2;
        obj[14] = CARRIED;
        put_line("Rybak daje ci ogromną siekierę.");
        return;
    }
    if (loc == 9 && strcmp(o2, "gówna") == 0) {
        if (obj[21] != CARRIED) {
            put_line("Za kantowanie - ścięcie łba!");
            die();
            return;
        }
        win();
        return;
    }
    put_line("To ci się chyba nie uda");
}

static void cmd_poop(void)
{
    if (sram == 1) {
        put_line("Ale nie chce ci się!");
        return;
    }
    if (loc == 17) {
        put_line("Gówno wpada z mokrym plaskiem do szamba.");
        return;
    }
    obj[17] = (unsigned char)loc;
    put_line("Zrobiłeś przepiękne gówno!");
    sram = 1;
}

static void cmd_wipe(void)
{
    if (obj[16] != CARRIED) {
        put_line("Czym ? Ręką ?!!");
        return;
    }
    if (sram != 1) {
        put_line("I po co ?");
        return;
    }
    put_line("SZURU - BURU !");
    obj[16] = NOWHERE;
    obj[18] = CARRIED;
    sram = 2;
}

static void cmd_sit(void)
{
    siad = 1;
    put_line("Ale dobrze...");
}

static void cmd_stand(void)
{
    siad = 0;
    put_line("Ok.");
}

static void cmd_buy(void)
{
    int i;
    if (!has_arg()) {
        put_line("Ale co ??");
        return;
    }
    if (loc != 22) {
        put_line("Przecież handel obwoźny jest zabroniony.");
        return;
    }
    for (i = 0; shop[i].name != NULL; i++) {
        if (strcmp(shop[i].name, o2) == 0)
            break;
    }
    if (shop[i].name == NULL) {
        put_line("Niestety ; tego nie prowadzimy.");
        return;
    }
    if (money - shop[i].price < 0) {
        put_line("Nie stać cię!");
        return;
    }
    money -= shop[i].price;
    put_str("Masz jeszcze ");
    put_num(money);
    put_line(" zł.");
    obj[6 + shop[i].offset] = CARRIED;
    cmd_inv();
}

static void cmd_chop(void)
{
    if (!has_arg()) {
        put_line("Ale co ??");
        return;
    }
    if (obj[14] != CARRIED) {
        put_line("Ciekawe czym ?");
        return;
    }
    if (strcmp(o2, "drzewo") == 0) {
        if (loc == 1) {
            drzewo = 1;
            put_line("Drzewo wali się z ogromnym hukiem , a   gówno z czubka spada u twych stóp.");
            obj[15] = 1;
            exits[4][1] = 0;
            return;
        }
        if (loc == 12) {
            put_line("Możesz sobie tak rąbać.Tu jest sporo    drzew.");
            return;
        }
        put_line("Coś ci odbiło ?");
    }
    put_line("Akurat !");
}

static void cmd_craft(void)
{
    if (strcmp(o2, "wędkę") != 0) {
        put_line("Sraty-taty.");
        return;
    }
    if (obj[5] != CARRIED || obj[10] != CARRIED || obj[11] != CARRIED) {
        put_line("Nie masz czym.");
        return;
    }
    obj[5] = NOWHERE;
    obj[10] = NOWHERE;
    obj[11] = NOWHERE;
    put_line("Masz teraz ogromne wędzisko!");
    obj[12] = CARRIED;
}

static void cmd_power(void)
{
    if (strcmp(o2, "prąd") != 0 && strcmp(o2, "wyłącznik") != 0 && has_arg()) {
        put_line("Nie uda ci się to.");
        return;
    }
    if (loc != 23) {
        put_line("Nie ma tu nigdzie wyłącznika");
        return;
    }
    if (prad == 1) {
        put_line("Ale już to zrobiłeś!!");
        return;
    }
    prad = 1;
    put_line("O.K.");
}

static void cmd_feed(void)
{
    if (!has_arg()) {
        put_line("Ale kogo ??");
        return;
    }
    if (strcmp(o2, "żula") == 0) {
        if (loc != 22)
            put_line("Tu nie ma takiego");
        else
            put_line("Nic z tego,dostałeś w ryło.");
        return;
    }
    if (strcmp(o2, "rybaka") == 0) {
        if (loc != 5 || rybak == 2)
            put_line("Tu nie ma takiego");
        else
            put_line("On nie jest głodny.");
        return;
    }
    if (strcmp(o2, "Eepcha") == 0 || strcmp(o2, "eepcha") == 0) {
        if (loc != 24) {
            put_line("A gdzie go widzisz ?");
            return;
        }
        if (obj[19] != CARRIED) {
            put_line("Ale nie masz dla niego żarcia!");
            return;
        }
        obj[19] = NOWHERE;
        obj[20] = (unsigned char)loc;
        put_line("Eepch się nażarł - i z wdzięczności     nasrał ci pod nogi !");
        return;
    }
    put_line("Ciekawe ,jak chcesz to zrobić!");
}

static void cmd_dig(void)
{
    if (obj[3] != CARRIED) {
        put_line("Ale czym ??");
        return;
    }
    if (loc != 4) {
        put_line("Wykopałeś przepiękny dołek.");
        return;
    }
    if (szkat == 1) {
        put_line("Masz zadatki na kopacza rowów.");
        return;
    }
    szkat = 1;
    put_line("Wykopałeś (oprócz kości) starą szkatułę.");
}

static void cmd_eat(void)
{
    if (!check() || !check_carried())
        return;
    put_line(eat_msg[item]);
    if (eat_consumes[item])
        obj[item] = NOWHERE;
}

static void init_game(void)
{
    memcpy(exits, exits_init, sizeof exits);
    memcpy(obj, obj_init, sizeof obj);
    loc = 0;
    tekst = 1;
    money = START_MONEY;
    zatoczka = rybak = sram = szkat = prad = gosp = drzewo = siad = 0;
    item = 0;
    page_count = 0;
    ended = 0;
    out_col = 0;
}

int main(void)
{
    for (;;) {
        init_game();
        location();
        while (!ended) {
            if (!gcom())
                return 0;
            ecom();
        }
        put_line("Grasz jeszcze raz (T/N)?");
        if (get_key() == 'N')
            return 0;
    }
}
