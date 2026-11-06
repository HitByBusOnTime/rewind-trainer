// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Online.API
{
    /// <summary>
    /// The API used by Rewind Trainer. It never makes a request, so this build can't log in or reach any server.
    /// </summary>
    public partial class OfflineAPIAccess : DummyAPIAccess
    {
        private const string message = "Rewind Trainer is an unofficial offline build and has no online features.";

        public OfflineAPIAccess()
        {
            SetState(APIState.Offline);
            LocalUser.Value = new GuestUser();
        }

        public override void Login(string username, string password)
        {
            LastLoginError = new APIException(message, null);
            SetState(APIState.Offline);
        }

        public override RegistrationRequest.RegistrationRequestErrors? CreateAccount(string email, string username, string password)
            => new RegistrationRequest.RegistrationRequestErrors { Message = message };
    }
}
