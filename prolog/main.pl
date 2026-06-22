% Simple SWI-Prolog starter file.
% Run a query in the terminal with:
%   swipl -s prolog/main.pl

parent(john, mary).
parent(mary, anne).

grandparent(X, Z) :-
    parent(X, Y),
    parent(Y, Z).

hello :-
    writeln('Hello from SWI-Prolog in VS Code!').
