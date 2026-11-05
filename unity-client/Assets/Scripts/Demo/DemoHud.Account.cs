using System;
using UnityEngine;

/// <summary>The account screens: sign in, create an account, reset the password, and the signed-in page.</summary>
public sealed partial class DemoHud
{
    private enum AccountView { SignIn, Register, Forgot, Profile, DeleteConfirm }

    private AccountView _accView = AccountView.SignIn;
    private string _accEmail = "", _accPassword = "", _accName = "", _accCode = "", _accNewPassword = "";
    private bool _accBusy, _accCodeSent, _accMessageIsError;
    private string _accMessage = "";
    private GUIStyle _accField;

    private void OpenAccountPage()
    {
        _page = MenuPage.Account;
        _accView = _game.Account.SignedIn ? AccountView.Profile : AccountView.SignIn;
        _accPassword = _accNewPassword = _accCode = "";
        _accCodeSent = false;
        _accMessage = "";
        if (_game.Account.SignedIn) _game.Account.RefreshMe();
    }

    private void AccountMessage(string text, bool error)
    {
        _accMessage = text;
        _accMessageIsError = error;
    }

    // Runs one account call with the "please wait" state around it; `ok` runs when it succeeded.
    private void AccountCall(Action<Action<ApiResult>> call, Action<ApiResult> ok)
    {
        _accBusy = true;
        AccountMessage("", false);
        call(result =>
        {
            _accBusy = false;
            if (result.Ok) ok(result);
            else AccountMessage(Strings.AccountError(result), true);
        });
    }

    private void DrawAccount()
    {
        float w = Screen.width, h = Screen.height;
        var title = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(h * 0.08f) };
        GUI.Label(new Rect(0f, h * 0.03f, w, h * 0.12f), Strings.Get("account.title"), title);
        if (_accField == null) _accField = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(h * 0.04f), alignment = TextAnchor.MiddleLeft, padding = new RectOffset(16, 16, 4, 4) };
        _accField.fontSize = Mathf.RoundToInt(h * 0.04f);

        GUI.enabled = !_accBusy;
        switch (_accView)
        {
            case AccountView.SignIn: DrawSignIn(); break;
            case AccountView.Register: DrawRegister(); break;
            case AccountView.Forgot: DrawForgot(); break;
            case AccountView.Profile: DrawAccountProfile(); break;
            case AccountView.DeleteConfirm: DrawDeleteConfirm(); break;
        }
        GUI.enabled = true;

        var msg = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.RoundToInt(h * 0.034f) };
        msg.normal.textColor = _accMessageIsError ? new Color(1f, 0.55f, 0.5f) : new Color(0.6f, 1f, 0.7f);
        string text = _accBusy ? Strings.Get("account.busy") : _accMessage;
        GUI.Label(new Rect(w * 0.2f, h * 0.8f, w * 0.6f, h * 0.12f), text, msg);

        var back = new Rect(w * 0.04f, h - h * 0.14f, w * 0.12f, h * 0.1f);
        if (GUI.Button(back, Strings.Get("menu.back"), _button)) _page = MenuPage.Main;
    }

    private Rect AccountColumn(float top) => new Rect(Screen.width * 0.25f, Screen.height * top, Screen.width * 0.5f, Screen.height * 0.085f);

    // A label above a text field; returns the bottom edge as a fraction of the screen height.
    private float AccountField(string label, ref string value, float top, bool password = false, int maxLength = 128)
    {
        float h = Screen.height;
        var small = new GUIStyle(_label) { alignment = TextAnchor.LowerLeft, fontSize = Mathf.RoundToInt(h * 0.03f) };
        Rect field = AccountColumn(top + 0.045f);
        GUI.Label(new Rect(field.x, h * top, field.width, h * 0.045f), Strings.Get(label), small);
        value = password ? GUI.PasswordField(field, value, '*', maxLength, _accField) : GUI.TextField(field, value, maxLength, _accField);
        return top + 0.045f + 0.085f + 0.02f;
    }

    private bool AccountButton(string key, float top, float leftFraction = 0.25f, float widthFraction = 0.5f)
    {
        float w = Screen.width, h = Screen.height;
        return GUI.Button(new Rect(w * leftFraction, h * top, w * widthFraction, h * 0.095f), Strings.Get(key), _button);
    }

    private void DrawSignIn()
    {
        float y = AccountField("account.email", ref _accEmail, 0.16f, maxLength: 254);
        y = AccountField("account.password", ref _accPassword, y, password: true);
        if (AccountButton("account.signIn", y + 0.01f, 0.25f, 0.24f)) SignInWithPassword();
        if (AccountButton("account.create", y + 0.01f, 0.51f, 0.24f)) { _accView = AccountView.Register; AccountMessage("", false); }
        if (AccountButton("account.forgot", y + 0.12f, 0.25f, 0.24f)) { _accView = AccountView.Forgot; _accCodeSent = false; AccountMessage("", false); }
        if (AccountButton("account.google", y + 0.12f, 0.51f, 0.24f)) SignInWithGoogle();

        var hint = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height * 0.028f) };
        GUI.Label(new Rect(Screen.width * 0.22f, Screen.height * (y + 0.24f), Screen.width * 0.56f, Screen.height * 0.1f), Strings.Get("account.guestHint"), hint);
    }

    private void DrawRegister()
    {
        float y = AccountField("account.email", ref _accEmail, 0.16f, maxLength: 254);
        y = AccountField("account.password", ref _accPassword, y, password: true);
        y = AccountField("account.name", ref _accName, y, maxLength: 24);
        if (AccountButton("account.create", y + 0.01f, 0.25f, 0.24f)) AccountCall(
            done => _game.Account.Register(_accEmail.Trim(), _accPassword, _accName.Trim(), done), _ => SignedIn());
        if (AccountButton("account.backToSignIn", y + 0.01f, 0.51f, 0.24f)) { _accView = AccountView.SignIn; AccountMessage("", false); }
    }

    private void DrawForgot()
    {
        float y = AccountField("account.email", ref _accEmail, 0.16f, maxLength: 254);
        if (!_accCodeSent)
        {
            if (AccountButton("account.sendCode", y + 0.01f, 0.25f, 0.24f)) AccountCall(
                done => _game.Account.ForgotPassword(_accEmail.Trim(), done),
                _ => { _accCodeSent = true; AccountMessage(Strings.Get("account.codeSent"), false); });
        }
        else
        {
            y = AccountField("account.code", ref _accCode, y, maxLength: 12);
            y = AccountField("account.newPassword", ref _accNewPassword, y, password: true);
            if (AccountButton("account.changePassword", y + 0.01f, 0.25f, 0.24f)) AccountCall(
                done => _game.Account.ResetPassword(_accEmail.Trim(), _accCode.Trim(), _accNewPassword, done),
                _ => { _accView = AccountView.SignIn; _accPassword = _accNewPassword = _accCode = ""; AccountMessage(Strings.Get("account.resetDone"), false); });
        }
        if (AccountButton("account.backToSignIn", y + 0.01f, 0.51f, 0.24f)) { _accView = AccountView.SignIn; AccountMessage("", false); }
    }

    private void DrawAccountProfile()
    {
        float h = Screen.height;
        AccountUser user = _game.Account.User;
        var line = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(h * 0.04f) };
        var small = new GUIStyle(line) { fontSize = Mathf.RoundToInt(h * 0.032f), wordWrap = true };
        float w = Screen.width;
        GUI.Label(new Rect(0f, h * 0.16f, w, h * 0.07f), Strings.Get("account.signedInAs", user?.displayName ?? ""), line);
        GUI.Label(new Rect(0f, h * 0.23f, w, h * 0.06f), user?.email ?? "", small);
        bool verified = user != null && user.emailVerified;
        GUI.Label(new Rect(0f, h * 0.29f, w, h * 0.06f), Strings.Get(verified ? "account.verified" : "account.unverified"), small);
        string sync = _game.Syncing ? Strings.Get("account.busy") : string.IsNullOrEmpty(_game.SyncStatusKey) ? "" : Strings.Get(_game.SyncStatusKey);
        GUI.Label(new Rect(0f, h * 0.35f, w, h * 0.06f), sync, small);

        float y = 0.45f;
        if (AccountButton("account.syncNow", y, 0.25f, 0.24f)) _game.SyncProfile();
        if (!verified && AccountButton("account.resend", y, 0.51f, 0.24f)) AccountCall(
            done => _game.Account.ResendVerification(done), _ => AccountMessage(Strings.Get("account.resent"), false));
        if (AccountButton("account.signOut", y + 0.12f, 0.25f, 0.24f)) AccountCall(
            done => _game.SignOut(() => done(new ApiResult { Status = 204 })), _ => { _accView = AccountView.SignIn; _accEmail = ""; });
        if (AccountButton("account.delete", y + 0.12f, 0.51f, 0.24f)) { _accView = AccountView.DeleteConfirm; _accPassword = ""; AccountMessage("", false); }
        GUI.Label(new Rect(w * 0.22f, h * (y + 0.24f), w * 0.56f, h * 0.1f), Strings.Get("account.signOutHint"), small);
    }

    private void DrawDeleteConfirm()
    {
        float h = Screen.height, w = Screen.width;
        var warn = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = Mathf.RoundToInt(h * 0.036f) };
        GUI.Label(new Rect(w * 0.2f, h * 0.16f, w * 0.6f, h * 0.12f), Strings.Get("account.deleteWarn"), warn);
        float y = 0.32f;
        if (_game.Account.User != null && _game.Account.User.hasPassword) y = AccountField("account.password", ref _accPassword, y, password: true);
        if (AccountButton("account.deleteConfirm", y + 0.02f, 0.25f, 0.24f)) AccountCall(
            done => _game.Account.DeleteAccount(_accPassword, done),
            _ => { _game.AfterAccountDeleted(); _accView = AccountView.SignIn; _accEmail = _accPassword = ""; });
        if (AccountButton("account.cancel", y + 0.02f, 0.51f, 0.24f)) { _accView = AccountView.Profile; AccountMessage("", false); }
    }

    private void SignInWithPassword() => AccountCall(done => _game.Account.Login(_accEmail.Trim(), _accPassword, done), _ => SignedIn());

    private void SignInWithGoogle()
    {
        _accBusy = true;
        AccountMessage("", false);
        GoogleSignIn.Request((idToken, error) =>
        {
            if (string.IsNullOrEmpty(idToken))
            {
                _accBusy = false;
                AccountMessage(Strings.AccountErrorByCode(error), true);
                return;
            }
            _accBusy = false;
            AccountCall(done => _game.Account.GoogleSignIn(idToken, done), _ => SignedIn());
        });
    }

    // Signed in or registered: join this phone's progress with the account's.
    private void SignedIn()
    {
        _accView = AccountView.Profile;
        _accPassword = _accNewPassword = "";
        AccountMessage("", false);
        _game.SyncProfile();
    }
}
