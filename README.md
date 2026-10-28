# Dirija! — MVP (Entrega 1: fluxo do aluno)

Marketplace que conecta quem está tirando a CNH (categorias A e B) a instrutores autônomos credenciados em São Paulo.

> **Versão de demonstração.** Usuários, instrutores e pagamentos são **fictícios/simulados**. Os dados ficam num banco SQLite local. Não use em produção.

## Como rodar

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/Dirija.Web
```

Acesse <http://localhost:5275>. Na primeira execução o banco é criado e populado automaticamente.

Testes:

```powershell
dotnet test
```

## Banco de dados (SQLite + EF Core)

- Arquivo: `src/Dirija.Web/App_Data/dirija.db` (ignorado pelo git). Configurado em `ConnectionStrings:Dirija` no `appsettings.json`.
- Na inicialização o app aplica as migrations, cria o seed na primeira vez (contas demo, 13 instrutores, horários, avaliações) e, nas próximas, só gera os horários que faltam para os próximos 14 dias.
- **Voltar ao estado inicial** (útil antes de gravar uma demo): apague o arquivo `dirija.db` (com o app parado) ou rode com `Database:ResetOnStartup=true`:

  ```powershell
  dotnet run --project src/Dirija.Web -- --Database:ResetOnStartup=true
  ```

- Nova migration depois de mudar o modelo:

  ```powershell
  dotnet tool restore
  dotnet ef migrations add NomeDaMudanca --project src/Dirija.Infrastructure --startup-project src/Dirija.Web --output-dir Persistence/Migrations
  ```

- Datas são gravadas em UTC e exibidas no horário de São Paulo. Senhas são guardadas com hash (PBKDF2 do ASP.NET Core Identity).
- Ida para PostgreSQL: trocar `UseSqlite` por `UseNpgsql` em `Dirija.Infrastructure/DependencyInjection.cs`, gerar migrations novas e ajustar a connection string (detalhes em `docs/plano-implementacao.md`).

## Contas demo

| E-mail              | Senha    |
|---------------------|----------|
| `aluno@dirija.com`  | `123456` |
| `aluna2@dirija.com` | `123456` |

Cartão de teste no checkout: `4111 1111 1111 1111`, qualquer validade futura, CVV `123`.

## Estrutura

```
src/Dirija.Core            entidades, interfaces, serviços (busca e reserva), fuso de SP
src/Dirija.Infrastructure  EF Core: DbContext, repositórios, migrations, seed, hash de senha
src/Dirija.Web             ASP.NET Core MVC (controllers, views Razor, Leaflet + Bootstrap)
tests/Dirija.Tests         xUnit: unidade e integração sobre SQLite real (inclui concorrência)
docs/                      plano de implementação (.md e .pdf)
```

Regras principais:
- Só instrutores com credencial verificada aparecem na busca (pré-filtro por área no banco + distância exata em C#).
- Ao escolher um horário, ele fica bloqueado por 10 min para o aluno pagar. Um horário nunca recebe duas reservas: controle de concorrência otimista (coluna `Version`) — testado com 50 reservas simultâneas.
- Taxa da plataforma configurável em `appsettings.json` (`Booking:PlatformFeePercent`, padrão 15%).

## Roteiro de demo (3 min)

1. Entre com `aluno@dirija.com`.
2. **Buscar:** digite "Moema", filtre "Categoria B" e "Até R$ 100". Pins mostram o preço; clique num pin para destacar o card.
3. **Ver perfil** de um instrutor: credencial verificada, veículo, ponto de encontro, área de atendimento e avaliações.
4. Escolha um horário → checkout com timer de 10 min.
5. *(Anti-overbooking)* Em uma janela anônima, entre com `aluna2@dirija.com` e tente o mesmo horário: ele é recusado.
6. Pague com PIX ("Já paguei") ou cartão de teste → **Aula confirmada**.
7. **Minhas aulas:** veja a aula em *Próximas*, cancele uma, e em *Histórico* use **Avaliar aula**.
8. **Perfil:** dados, métodos de pagamento, configurações, Sobre e Sair.

## Próximos passos

PostgreSQL/PostGIS, cadastro de usuários, painel do instrutor, painel do admin (aprovação de credenciais), gateway de pagamento real com split e notificações.
