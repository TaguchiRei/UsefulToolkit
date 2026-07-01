using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UsefulToolkit.Attributes;

public class JankenHTTPRequester : MonoBehaviour
{
    private static readonly byte[] Key =
        Encoding.UTF8.GetBytes("12345678901234567890123456789012");

    // 16バイト
    private static readonly byte[] IV =
        Encoding.UTF8.GetBytes("1234567890123456");

    private const string API_URL = "http://10.40.12.231/GameApi/janken_api.php";
    private const string PlayerWinKey = "PLAYER_WIN";
    private const string PlayerLoseKey = "PLAYER_LOSE";

    [SerializeField] private TextMeshProUGUI _playerHand;
    [SerializeField] private TextMeshProUGUI _cpuHand;
    [SerializeField] private TextMeshProUGUI _winLose;
    [SerializeField] private TextMeshProUGUI _winCountText;
    [SerializeField] private TextMeshProUGUI _loseCountText;
    [SerializeField] private TextMeshProUGUI _jankenponText;

    private int _winCount = 0;
    private int _loseCount = 0;

    private Phase _phase = Phase.Pon;
    private JankenHand _enemyHand = JankenHand.Gu;

    private void Start()
    {
        Reset();
        var winCount = PlayerPrefs.GetString(PlayerWinKey, string.Empty);
        var loseCount = PlayerPrefs.GetString(PlayerLoseKey, string.Empty);

        if (winCount != string.Empty)
        {
            winCount = Decrypt(winCount);
        }
        else
        {
            winCount = "0";
        }

        if (loseCount != string.Empty)
        {
            loseCount = Decrypt(loseCount);
        }
        else
        {
            loseCount = "0";
        }

        _winCount = int.Parse(winCount);
        _loseCount = int.Parse(loseCount);
    }


    private void FixedUpdate()
    {
        if (_phase == Phase.Ken)
        {
            //cpuの手をアニメーションさせる
            _enemyHand = (JankenHand)(((int)_enemyHand + 1) % 3);
            _cpuHand.text = _enemyHand.ToString();
        }
    }

    private void ResetPlayerPrefs()
    {
        PlayerPrefs.DeleteKey(PlayerLoseKey);
        PlayerPrefs.DeleteKey(PlayerWinKey);
    }

    /// <summary>
    /// じゃんけん処理をUIのButtonから呼び出す
    /// </summary>
    /// <param name="hand"></param>
    public void CallJanken(int hand)
    {
        if (_phase != Phase.Jan) return;
        //プレイヤーの手を入力で固定
        _playerHand.text = ((JankenHand)hand).ToString();
        _jankenponText.text = "ken";
        Janken(hand).Forget();
    }

    /// <summary>
    /// じゃんけんのフェーズをリセットする
    /// </summary>
    public void Reset()
    {
        if (_phase != Phase.Pon) return;
        _jankenponText.text = "jan";
        _phase = Phase.Jan;
    }

    /// <summary>
    /// じゃんけん結果を取得し、UIに反映する
    /// </summary>
    /// <param name="hand"></param>
    private async UniTask Janken(int hand)
    {
        //APIを呼ぶ
        var result = await CallAPI(hand);
        //フェーズをKenにして１秒待機
        _phase = Phase.Ken;
        await UniTask.WaitForSeconds(1f);

        //じゃんけん結果をUIに表示する
        _jankenponText.text = "pon";
        _phase = Phase.Pon;
        _cpuHand.text = ((JankenHand)result.CpuHand).ToString();
        _winLose.text = result.WinLose.ToString();

        //結果に応じて勝敗数を保存
        if (result.WinLose == JankenResult.Win)
        {
            _winCount++;
            PlayerPrefs.SetString(PlayerWinKey, Encrypt(_winCount.ToString()));
        }
        else if (result.WinLose == JankenResult.Lose)
        {
            _loseCount++;
            PlayerPrefs.SetString(PlayerLoseKey, Encrypt(_loseCount.ToString()));
        }

        //勝敗数表示
        _winCountText.text = _winCount.ToString();
        _loseCountText.text = _loseCount.ToString();
    }

    /// <summary>
    /// じゃんけんのAPIを呼び出し、構造体の形式に変換
    /// </summary>
    /// <param name="hand"></param>
    /// <returns></returns>
    private async UniTask<JankenResponse> CallAPI(int hand)
    {
        string url = $"{API_URL}?hand={hand}";

        using var request = UnityWebRequest.Get(url);
        await request.SendWebRequest();
        Debug.Log(request.downloadHandler.text);

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"エラー: {request.error}");
            return default;
        }

        var json = Decrypt(request.downloadHandler.text);
        Debug.Log(json);

        return JsonUtility.FromJson<JankenResponse>(json);
    }

    static string Encrypt(string plainText)
    {
        using Aes aes = Aes.Create();

        aes.Key = Key;
        aes.IV = IV;

        using MemoryStream ms = new();
        using CryptoStream cs = new(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
        using StreamWriter sw = new(cs);

        sw.Write(plainText);
        sw.Close();

        return Convert.ToBase64String(ms.ToArray());
    }

    static string Decrypt(string cipherText)
    {
        using Aes aes = Aes.Create();

        aes.Key = Key;
        aes.IV = IV;

        byte[] buffer = Convert.FromBase64String(cipherText);

        using MemoryStream ms = new(buffer);
        using CryptoStream cs = new(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using StreamReader sr = new(cs);

        return sr.ReadToEnd();
    }
}

/// <summary>
/// じゃんけんの結果を纏めるための構造体
/// </summary>
[Serializable]
public struct JankenResponse
{
    public int PlayerHand;
    public int CpuHand;
    public JankenResult WinLose;

    public JankenResponse(int playerHand, int cpuHand, JankenResult winLose)
    {
        PlayerHand = playerHand;
        CpuHand = cpuHand;
        WinLose = winLose;
    }
}

public enum Phase
{
    Jan,
    Ken,
    Pon
}

public enum JankenHand
{
    Gu = 0,
    Pa = 1,
    Choki = 2,
}

public enum JankenResult
{
    Draw = 0,
    Win = 1,
    Lose = 2,
}