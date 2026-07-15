namespace HonzaBotner.Services.Contract;

public interface IHashService
{
    string Hash(string input);
    string LegacyHash(string input);
}
