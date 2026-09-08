namespace backend.Services;

public interface IEmailService
{
    Task SendVerificationOtpAsync(string toEmail, string recipientName, string otpCode);
}
