namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// 表示の文字の翻訳表（UdonSharpBehaviour ではない、値を返すだけのクラス）。
    ///
    /// - 1列目は日本語の原文。パネル・タブレットの文字が原文と完全に一致したときだけ、各言語の文に差し替える
    ///   （改行・空白・リッチテキストのタグも含めて一致させる）。一致しない文字（ワールド作者が書き換えた文言・
    ///   音色名・数字など）はそのまま表示する
    /// - 言語を足すときは、Languages() に言語コードを足し、Table() の各行の末尾に同じ並びで1列足す。
    ///   空文字の欄は原文（日本語）のまま表示する
    /// - 言語コードは VRChat の VRCPlayerApi.GetCurrentLanguage() が返す形（en / ja / ko / zh-CN など）
    /// - 音色名はここに入れない。音色（NadeSoundPreset）の Name Languages / Localized Names に入れる
    /// - 生成ツールの文言を変えたら、ここの原文も合わせる。
    ///   Tools > Silent Comfort > World Nadenade (Dev) > Check Translations で、配布 Prefab の文字に抜けが無いか確かめられる
    /// </summary>
    public static class NadeStrings
    {
        /// 列の並び。先頭は原文の日本語にする
        public static string[] Languages()
        {
            return new string[] { "ja", "en" };
        }

        /// 翻訳表。Languages() の数ずつで1行
        public static string[] Table()
        {
            return new string[]
            {
                // ───── 共通（操作パネル・タブレット） ─────
                "なでなで音", "Head Pat Sounds",
                "聞こえる音", "What You Hear",
                "なでる音", "Your Pat Sound",
                "音量", "Volume",
                "音色", "Sound",
                "手のひらと手の甲で音が変わる", "Palm and back differ",
                "手のひら・手の甲", "Palm / Back of Hand",
                "向きで切替", "Switch by Direction",
                "手のひらで統一", "Always Palm",
                "手の甲で統一", "Always Back",
                "手のひらと手の甲を入れ替える", "Swap palm and back of hand",
                "音が逆に聞こえるときに", "If the sounds seem reversed",
                "統一しているときは使えません", "Not available when fixed to one side",
                "この音色は手のひらと手の甲が同じ音です", "This sound is the same for palm and back",
                "未接続：Manager を設定してください", "Not connected: set the Manager",
                "未接続", "Not connected",

                // ───── 操作パネル ─────
                "使い方", "How to Use",
                "閉じる", "Close",
                "自分の端末だけの設定です", "Only on your device",
                "相手にも同じ音が聞こえます", "Others hear it too",
                "なで音を聞く", "Hear Pat Sounds",
                "オンにした人にだけ聞こえます", "Only people who turn this on hear them",
                "自分なで", "Self Pat",
                "自分の頭をなでても鳴らす", "Play sounds when patting your own head",
                "VRChat のワールド音量は 50% 以上がおすすめです", "We recommend a VRChat world volume of 50% or higher",
                "なで音：ON", "Pat sounds: ON",
                "なで音：OFF", "Pat sounds: OFF",
                "自分なで：ON", "Self pat: ON",
                "自分なで：OFF", "Self pat: OFF",
                "音量 ", "Volume ",
                "音色：", "Sound: ",

                // ───── 操作パネルの使い方 ─────
                "なで音をオンにする", "Turn on pat sounds",
                "なでる人も、なでられる人も、それぞれ「なで音を聞く」をオンにすると聞こえます。",
                "Both the person patting and the person being patted need to turn on “Hear Pat Sounds” to hear them.",
                "頭をなでる", "Pat a head",
                "手で相手の頭に触れて動かすと鳴ります。速く動かすほど、音が大きくなります。",
                "Touch someone's head with your hand and move it. The faster you move, the louder the sound.",
                "音量を合わせる", "Adjust the volume",
                "音量は自分にだけ効きます。小さいときは VRChat のワールド音量を 50% 以上にしてください。",
                "The volume only affects you. If it is too quiet, set VRChat's world volume to 50% or higher.",
                "なでる音を選ぶ", "Choose your pat sound",
                "音色を押すと、自分にだけ試し聴きが流れます。選んだ音は、なでた相手にも聞こえます。",
                "Press a sound to preview it just for yourself. The person you pat hears the sound you choose.",
                "音が逆に聞こえるとき", "If the sounds seem reversed",
                "手のひらでなでているのに手の甲の音がするときは、入れ替えをオンにします。",
                "If you hear the back-of-hand sound while patting with your palm, turn on the swap.",
                "一人で試す", "Try it alone",
                "「自分なで」をオンにすると、自分の頭をなでても鳴ります。",
                "Turn on “Self Pat” to hear sounds when you pat your own head.",
                "タブレットが見つからないときは、置き台の「タブレットを戻す」を押すと置き台に戻ります。",
                "Can't find the tablet? Press “Return Tablet” on the stand to bring it back.",

                // ───── タブレット・置き台 ─────
                "自分の耳に届く音の大きさです（なでられているときも、なでているときも）。ほかの人には影響しません。",
                "How loud pat sounds are for you. It doesn't affect anyone else.",
                "あなたの手でなでたときの音です。なでた相手にも同じ音が聞こえます。",
                "The sound your hands make. The person you pat hears it too.",
                "なで音はオフです\n<size=70%>操作パネルでオンにすると使えます</size>",
                "Pat sounds are off\n<size=70%>Turn them on at the control panel</size>",
                "タブレットを戻す", "Return Tablet",
                "押すと、持ち出したタブレットがここに戻ります", "Press to bring the tablet back here",
                "タブレット", "Tablet",
                "タップ", "Tap",
            };
        }
    }
}
