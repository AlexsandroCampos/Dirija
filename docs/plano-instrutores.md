# Plano: instrutores, administrador, cadastro e cancelamento

Branch `feat/instrutores-admin` (a partir da `main`, commit `db813cb`). Mesma arquitetura: regras no **Core**, dados no **Infrastructure**, telas no **Web**. Continua em SQLite, sem recursos específicos dele.

## Decisões

| # | Decisão |
|---|---|
| D1 | O aluno pode cancelar a menos de 24 h da aula, **sem reembolso**. Com 24 h ou mais: reembolso integral (simulado). Pagamento pendente: cancela a qualquer momento |
| D2 | A localização do instrutor é o **ponto de encontro**: ele escolhe o bairro, arrasta o marcador no mapa e descreve o local. Distância e raio passam a ser medidos a partir desse ponto. O ponto precisa estar no município de São Paulo; a tela avisa para usar um local público |
| D3 | Contas demo `admin@`, `instrutor@` (Carlos Mendes, aprovado) e `pendente@` (Tiago Ramos, pendente), senha `123456`. `Demo:Enabled=false` desliga todos os dados de demonstração |
| D4 | O instrutor vê nome e telefone do aluno só em reservas confirmadas e futuras |
| D5 | Janela de reserva de **60 dias** e antecedência mínima de **6 horas** (referências: Calendly, padrão de 60 dias; EzLicence, 75 dias e 5 h). Valores em `appsettings.json` |

## Agenda do instrutor

- **Disponibilidade semanal**: até duas faixas por dia da semana (ex.: seg a sex 8h–12h e 13h–18h). O sistema gera horários de 50 min a cada hora cheia dentro das faixas e mantém a janela de 60 dias preenchida.
- **Bloqueios (exceções)**: período com data, faixa de horário ou dia inteiro e anotação privada (ex.: "exame médico"). Vários bloqueios são permitidos. Um toque bloqueia um horário isolado. Bloqueios são registros próprios e continuam valendo quando novos dias entram na janela ou quando a disponibilidade muda.
- **Reservas dentro do bloqueio**: nada é cancelado automaticamente. A tela lista as aulas afetadas e o instrutor escolhe cancelá-las (reembolso integral, motivo obrigatório) ou mantê-las.
- **Horário extra**: abrir um horário fora do padrão (ex.: um domingo).
- **Mudança na disponibilidade**: horários futuros livres são refeitos; reservados ficam como estão.
- Bloqueio e reserva simultâneos do mesmo horário: só um vence (coluna `Version`).
- Os instrutores de exemplo sem conta continuam com a agenda fictícia de 14 dias.

## Fluxos

- **Aluno**: Criar conta → "Sou aluno" → dados → entra na busca.
- **Instrutor**: Criar conta → "Sou instrutor" → dados → etapa 2 (credencial, categorias, preço, veículo, raio, ponto de encontro, descrição) → painel "Cadastro em análise" (já pode montar a agenda; não aparece na busca).
- **Admin**: Pendentes → Aprovar ou Recusar (motivo obrigatório). Recusado corrige e reenvia; mudar credencial ou categorias depois de aprovado volta para pendente.
- **Instrutor cancela uma aula**: até o início, com motivo; aluno recebe reembolso integral e vê o motivo; o horário fica bloqueado.

## Segurança

Login por cookie (HttpOnly, SameSite=Lax, `Secure` fora de Development), antiforgery em todos os POST, senha com hash PBKDF2, mínimo 8 caracteres com letras e números, bloqueio de 15 min após 5 senhas erradas com mensagem genérica, perfis por `[Authorize(Roles)]`, status de aprovação lido do banco, admin real criado por `Admin:Email`/`Admin:Password` (variável de ambiente ou user-secrets), cada tela confere a posse dos dados.

## Fora deste pacote

PostgreSQL, pagamento real, e-mail (confirmação, recuperação de senha, lembretes), consulta ao Detran, foto, remarcação, exclusão de conta, suspensão de instrutor aprovado, relatórios financeiros, prazos configuráveis por instrutor.
