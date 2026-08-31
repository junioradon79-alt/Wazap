namespace Wazap.Application.Abstractions;

public interface IWhatsAppSender
{
    Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables);
    Task SendTextMessageAsync(string toPhoneNumber, string message);
}
