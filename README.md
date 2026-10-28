# Dirija! — MVP (Entrega 1: fluxo do aluno)

Marketplace que conecta quem está tirando a CNH (categorias A e B) a instrutores autônomos credenciados em São Paulo.

> **Versão de demonstração.** Usuários, instrutores e pagamentos são **fictícios/simulados** e ficam apenas em memória (os dados voltam ao estado inicial quando o app reinicia). Não use em produção.

## Como rodar

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/Dirija.Web
```

Acesse <http://localhost:5275>.

Testes:

```powershell
dotnet test
```

## Contas demo

| E-mail              | Senha    |
|---------------------|----------|
| `aluno@dirija.com`  | `123456` |
| `aluna2@dirija.com` | `123456` |

Cartão de teste no checkout: `4111 1111 1111 1111`, qualquer validade futura, CVV `123`.

## Estrutura

```
src/Dirija.Core   entidades, interfaces, serviços (busca e reserva), repositórios em memória e seed de SP
src/Dirija.Web    ASP.NET Core MVC (controllers, views Razor, Leaflet + Bootstrap)
tests/Dirija.Tests  xUnit: unidade (busca, reserva, avaliações) e integração (fluxo completo)
docs/             plano de implementação (.md e .pdf)
```

Regras principais:
- Só instrutores com credencial verificada aparecem na busca.
- Ao escolher um horário, ele fica bloqueado por 10 min para o aluno pagar; um horário nunca recebe duas reservas (lock no `BookingService`).
- Taxa da plataforma configurável em `appsettings.json` (`Booking:PlatformFeePercent`, padrão 15%).

## Roteiro de demo (3 min)

1. Entre com `aluno@dirija.com`.
2. **Buscar:** digite "Moema", filtre "Categoria B" e "Até R$ 100". Pins mostram o preço; clique num pin para destacar o card.
3. **Ver perfil** de um instrutor: credencial verificada, veículo, ponto de encontro, área de atendimento e avaliações.
4. Escolha um horário → checkout com timer de 10 min.
5. *(Anti-overbooking)* Em uma janela anônima, entre com `aluna2@dirija.com` e tente o mesmo horário: ele não aparece mais / é recusado.
6. Pague com PIX ("Já paguei") ou cartão de teste → **Aula confirmada**.
7. **Minhas aulas:** veja a aula em *Próximas*, cancele uma, e em *Histórico* use **Avaliar aula**.
8. **Perfil:** dados, métodos de pagamento, configurações, Sobre e Sair.

## Próximos passos

EF Core + PostgreSQL/PostGIS, painel do instrutor, painel do admin (aprovação de credenciais), gateway de pagamento real com split e notificações.
