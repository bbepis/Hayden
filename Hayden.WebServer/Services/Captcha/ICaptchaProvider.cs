using System.Threading.Tasks;

namespace Hayden.WebServer.Services.Captcha
{
    public interface ICaptchaProvider
    {
        Task<bool> VerifyCaptchaAsync(string response);
    }
}
