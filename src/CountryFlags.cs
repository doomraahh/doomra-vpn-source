using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
namespace Luna {
 internal static class CountryFlags {
  static readonly Dictionary<string,Image> images=new Dictionary<string,Image>(StringComparer.OrdinalIgnoreCase);
  static readonly Dictionary<string,string> names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  static readonly List<KeyValuePair<string,string>> aliases=new List<KeyValuePair<string,string>>();
  static readonly Dictionary<string,string> resolved=new Dictionary<string,string>();
  static CountryFlags() {
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Doomra.Flags"))
   using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
    foreach(var entry in zip.Entries) {
     if(Regex.IsMatch(entry.Name,@"^[a-z]{2}\.png$")) using(var data=entry.Open()) using(var image=Image.FromStream(data)) images[Path.GetFileNameWithoutExtension(entry.Name).ToUpperInvariant()]=new Bitmap(image);
    }
    var countries=zip.GetEntry("countries.json"); if(countries!=null) using(var reader=new StreamReader(countries.Open())) {
     foreach(var pair in Json.Read<Dictionary<string,string>>(reader.ReadToEnd())) { names[pair.Key]=pair.Value; aliases.Add(new KeyValuePair<string,string>(pair.Value.ToLowerInvariant(),pair.Key)); }
    }
   }
   string[,] extra={
    {"US","usa|unitedstates|america|сша|соединенные штаты|америка"},
    {"GB","uk|unitedkingdom|great britain|england|британия|великобритания|англия"},
    {"DE","deutschland|германия"},{"NL","holland|нидерланды|голландия"},{"FI","финляндия"},{"FR","франция"},{"SE","швеция"},{"NO","норвегия"},{"DK","дания"},
    {"CA","канада"},{"JP","япония"},{"SG","сингапур"},{"CH","швейцария"},{"AT","австрия"},{"PL","польша"},{"CZ","czechia|чехия"},
    {"ES","испания"},{"IT","италия"},{"PT","португалия"},{"TR","turkiye|türkiye|турция"},{"RU","russia|россия"},{"UA","украина"},{"BY","беларусь"},{"KZ","казахстан"},
    {"AM","армения"},{"GE","грузия"},{"AZ","азербайджан"},{"AE","uae|оаэ|эмираты"},{"IL","израиль"},{"AU","австралия"},{"NZ","новая зеландия"},
    {"HK","hongkong|гонконг"},{"KR","south korea|корея"},{"TW","тайвань"},{"CN","китай"},{"IN","индия"},{"ID","индонезия"},{"TH","таиланд|тайланд"},
    {"VN","вьетнам"},{"BR","бразилия"},{"AR","аргентина"},{"MX","мексика"},{"ZA","юар"},{"IS","исландия"},{"IE","ирландия"},{"EE","эстония"},
    {"LV","латвия"},{"LT","литва"},{"RO","румыния"},{"BG","болгария"},{"GR","греция"},{"HU","венгрия"},{"RS","сербия"},{"MD","молдова"},
    {"EU","europe|европа|евросоюз"}
   };
   foreach(var code in Enumerable.Range(0,extra.GetLength(0))) foreach(string alias in extra[code,1].Split('|')) aliases.Add(new KeyValuePair<string,string>(alias,extra[code,0]));
   aliases.Sort((a,b)=>b.Key.Length.CompareTo(a.Key.Length));
  }
  static bool Has(string text,string word,bool host) {
   return Regex.IsMatch(text,@"(?<![\p{L}\p{N}])"+Regex.Escape(word)+(host?@"\d*(?![\p{L}])":@"(?![\p{L}\p{N}])"),RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
  }
  static string Find(string text,bool host) {
   string original=text??"";
   text=original.ToLowerInvariant().Replace('ё','е');
   if(!host) {
    for(int i=0;i+3<text.Length;i++) {
     if(!char.IsHighSurrogate(text[i]) || !char.IsLowSurrogate(text[i+1]) || !char.IsHighSurrogate(text[i+2]) || !char.IsLowSurrogate(text[i+3])) continue;
     int a=char.ConvertToUtf32(text,i)-0x1f1e6,b=char.ConvertToUtf32(text,i+2)-0x1f1e6;
     if(a>=0 && a<26 && b>=0 && b<26) { string code=""+(char)('A'+a)+(char)('A'+b); if(images.ContainsKey(code)) return code; }
    }
   }
   foreach(var alias in aliases) if(Has(text,alias.Key,host)) return alias.Value;
   foreach(string code in images.Keys.OrderBy(x=>x,StringComparer.Ordinal)) if(host?Has(text,code,true):Regex.IsMatch(original,@"(?<![\p{L}\p{N}])"+code+@"(?![\p{L}\p{N}])",RegexOptions.CultureInvariant)) return code;
   return "";
  }
  internal static string Resolve(string name,string host) {
   name=name??""; host=host??""; string key=name.Length.ToString(CultureInfo.InvariantCulture)+":"+name+host,code;
   lock(resolved) if(resolved.TryGetValue(key,out code)) return code;
   code=Find(name,false); if(code=="") code=Find(host,true);
   lock(resolved) { if(resolved.Count>=4096) resolved.Clear(); resolved[key]=code; } return code;
  }
  static readonly Dictionary<string,Image> backgrounds=new Dictionary<string,Image>();
  internal static Image GetBackground(string code) {
   if(string.IsNullOrEmpty(code)) return null;
   lock(backgrounds) {
    Image found; if(backgrounds.TryGetValue(code,out found)) return found;
    found=null;
    foreach(string resource in new[]{"Doomra.CustomFlagBackgrounds","Doomra.FlagBackgrounds"}) {
     using(var stream=typeof(CountryFlags).Assembly.GetManifestResourceStream(resource)) {
      if(stream==null) continue;
      using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
       var entry=zip.GetEntry(code.ToLowerInvariant()+".png"); if(entry==null) continue;
       using(var bytes=entry.Open()) using(var image=Image.FromStream(bytes)) found=new Bitmap(image);
      }
     }
     if(found!=null) break;
    }
    if(found==null) return Get(code);
    // Drop references rather than disposing images that another paint may still use.
    if(backgrounds.Count>=4) backgrounds.Clear(); backgrounds[code]=found; return found;
   }
  }
  internal static Image Get(string code) { Image image; return code!=null && images.TryGetValue(code,out image)?image:null; }
 }
}
