# RabbitMQLearning

## Podstawowe informacje

RabbitMQ to broker wiadomości.

Podstawowe pojęcia:
- **Producing** - przesyłanie wiadomości. Program, który przesyła wiadomość to **Producer**
- **Queue** - kolejka, w której przechowywane są wiadomości
- **Consuming** - otrzymywanie wiadomości. Program, który czeka na wiadomości na **Consumer**
Aplikacja może być jednocześnie producerem i consumerem.

Najprostszą formą komunikacji jest przesłanie po prostu wiadomości z jednego producera do jednego consumera - Aplikacja 1.
![[Pasted image 20251206180628.png]]
Gdy deklarujemy kolejkę tworzona jest ona, jeżeli nie istnieje. Czyli np można mieć kod odpowiedzialny za deklarowanie kolejki i w producerze, i w consumerze, ale pierwszy który zostanie odpalony postawi kolejkę.

Możliwe jest też posiadanie większej ilości consumerów - Aplikacja 2
![[Pasted image 20251206181133.png]]
Standardowo jeżeli mielibyśmy dwóch consumerów to brałyby one taski naprzemiennie.

Jeżeli consumer, który dostal zadanie będzie miał problem po drodze, musimy zabezpieczyć task aby go nie utracić. W tym celu możemy uzyć ack(nowledgement). Wtedy consumer powinien zwrócić ack. Jak nie zwróci to wiadomo że coś jest nie tak i to zadanie może być przekazane do innego consumera.

Możliwe są też problemy z działaniem RabbitMQ servera. Aby temu zapobiec należy odpowiednio ustawić parametr durable dla queue na true, wtedy nawet jak coś się stanie to nie utracimy tasków w kolejce. Jeżeli kolejka o danej nazwie została już utworzona z durable: false, wtedy nie możemy zmienić tego na true i odwrotnie.

Może się też czasem zdażyć, że jeden z consumerów ciągle dostaje dłuższe zadania, a drugi ma luzik. Można to poprawić dodając parametr prefetchCount = 1 w callu do BasicQosAsync. wtedy dany consumer może mieć przypisane jedno zadanie jednocześnie, więc dostanie kolejne jeżeli skończy aktualne.

## Exchange

Dodanie exchange, czyli bloku X na obrazku sprawia, że możemy zarządzać do której kolejki trafią dane taski.
![[Pasted image 20251206182203.png]]

**Fanout** (Aplikacja 3) - przesyła do wszystkich kolejek podłączonych do tego exchange.
- Exchange name - dowolna nazwa
- Routing key - jest ignorowany, ustawiamy pusty string

**Direct** (Aplikacja 4) - przesyła do kolejek o dokładnie takim routingKey, jak podany w publish
- Exchange name - dowolna opisowa nazwa
- RoutingKey (przy publish) - musi być uzupełnione, coś konkretnego
- RoutingKey (przy bind) - identyczny jak przy publish

**Topic** (Aplikacja 5) - przesyła do tych kolejek, które mają odpowiedni routingKey, ale te routingKey mają formę reguł
- Exchange name - dowolna opisowa nazwa
- RoutingKey (przy publish) - musi być uzupełnione, coś konkretnego, jak tworzyć te nazwy opisane niżej
- RoutingKey (przy bind) - reguła, opisane niżej

Tworzenie routingKey:
Patterny używają znaków specjalnych: * oraz #
Najłatwiej na przykładach:

| Producer                                                                        | Consumer                             |
| ------------------------------------------------------------------------------- | ------------------------------------ |
| wszystkie zadziałają                                                            | #                                    |
| wszystkie, które zaczynają się od test. czyli np test.a albo test.a.a           | test.#                               |
| tylko te, które zaczynają się od test. a po kropce mają jedno 'słowo' np test.a | test.*                               |
| wszystkie, które kończą się z .test czyli np a.a.test, a.test                   | #.test                               |
| wszystkie, które mają jedno słowo przed . i kończą się na test np a.test        | *.test                               |
|                                                                                 | ![[Pasted image 20251206183313.png]] |
Przykłady działania z aplikacji 5:
![alt text](image.png)
![[Pasted image 20251206183535.png]]

## Remote Procedure Call

RPC w RabbitMQ to wzorzec komunikacji, który pozwala jednemu serwisowi wywołać metodę w innym serwisie tak, jakby była lokalna — ale komunikacja odbywa się asynchronicznie przez RabbitMQ (Aplikacja 6).

RabbitMQ jest z natury **asynchroniczny** (event-driven).  
RPC to sposób, aby "udawać" synchroniczną komunikację:
- Klient wysyła żądanie
- Czeka na odpowiedź
- Ale wszystko dalej jest oparte o kolejki i wiadomości

Jak to działa w uproszczeniu:
1. Klient (np. Twój serwis w C#)
	- Wysyła wiadomość do kolejki _RPC queue_.
	- Do tej wiadomości dodaje nagłówki:
	    - **reply_to** – nazwa kolejki, do której serwer ma odesłać odpowiedź.
	    - **correlation_id** – unikalny identyfikator, żeby klient wiedział, której odpowiedzi dotyczy.

 2. Serwer (worker)
	- Nasłuchuje kolejki RPC.
	- Otrzymuje wiadomość, wykonuje logikę.
	- Wysyła odpowiedź na kolejkę podaną w _reply_to_, kopiując _correlation_id_.

3. Klient
	- Nasłuchuje w swojej kolejce odpowiedzi.
	- Dopasowuje odpowiedź po **correlation_id**.
	- Oddaje wynik wywołania RPC jako rezultat swojej metody.

|Cecha|Bazowe RabbitMQ|RPC w RabbitMQ|
|---|---|---|
|Odpowiedź|❌ brak|✅ jest, obowiązkowa|
|Charakter|jednorazowa wiadomość|żądanie → odpowiedź|
|Typ komunikacji|asynchroniczna|pseudo-synchroniczna|
|Kolejka reply_to|❌ nieużywana|✅ obowiązkowa|
|correlation_id|❌ zbędny|✅ dopasowuje request–response|
|Trwałość|zwykle durable, event-driven|zwykle ephemeral (per-request)|
|Zastosowania|eventy, procesy, batch|request/response, obliczenia|

## Analiza Aplikacji 6

1 Co robi **klient RPC**?
Klient działa jak “zdalne wywołanie funkcji”.  
Wysyła wiadomość z parametrem (np. liczba `30`), a następnie czeka na odpowiedź.

Kluczowe elementy:
- **correlationId** — unikalne ID, by dopasować odpowiedź do zapytania
- **ReplyTo** — nazwa kolejki, na którą serwer ma odesłać wynik
- **TaskCompletionSource** — pozwala czekać asynchronicznie na odpowiedź

Przebieg:
1. Klient tworzy tymczasową kolejkę (np. `"amq.gen-XYZ"`).
2. Publikuje wiadomość na `"rpc_queue"` z:
   - parametrem (np. `"30"`)
   - `correlationId`
   - `ReplyTo = tymczasowa kolejka`
3. Czeka, aż do tej kolejki przyjdzie odpowiedź z pasującym `correlationId`.
4. Dostaje wynik i oddaje go do programu jako string.

2 Co robi **serwer RPC**?
Serwer nasłuchuje na kolejce `"rpc_queue"`.
Gdy dostanie wiadomość:
1. Odczytuje argument (`30`).
2. Przetwarza dane (tu - oblicza **Fib(n)**).
3. Tworzy odpowiedź:
   - ustawia **CorrelationId = takie samo jak w żądaniu**
   - wysyła wynik na kolejkę z **props.ReplyTo**
4. Wysyła ACK (potwierdzenie odbioru wiadomości).

3 Jak to działa razem?
Cały przepływ wygląda tak:
1. **Klient** wysyła:  
   - “Oblicz Fib(30)”  
   - na `"rpc_queue"`  
   - z `ReplyTo="amq.gen-XYZ"`  
   - z `CorrelationId="abcd-123"`

2. **Serwer**:
   - odbiera
   - liczy wynik
   - odsyła na `"amq.gen-XYZ"` wiadomość z tym samym `CorrelationId`

3. **Klient**:
   - sprawdza kolejkowane odpowiedzi
   - znajduje tę z `CorrelationId="abcd-123"`
   - zwraca wynik użytkownikowi

