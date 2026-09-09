using System.Text;

// 起動時に1回。呼ばないと CP932（半角カナ・JIS X 0201 等）が扱えない。
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// SCP 本体（C-ECHO / C-FIND / アソシエーション制御）は T3 以降で実装する。
Console.WriteLine("Ascube.Mwm.Scp: skeleton only (T1). SCP body is implemented from T3 onward.");
