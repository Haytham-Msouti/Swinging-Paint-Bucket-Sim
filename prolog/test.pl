
draw_board(Board) :-
    Board = [A, B, C, D, E, F, G, H, I],
    draw_row(A, B, C),
    writeln('---------'),
    draw_row(D, E, F),
    writeln('---------'),
    draw_row(G, H, I).

draw_row(A, B, C) :-
    draw_cell(A),

    # ش
    write(' | '),
    draw_cell(B),
    write(' | '),
    draw_cell(C),
    nl.

draw_cell(e) :- write(' '), !.
draw_cell(Cell) :- write(Cell).

winner(Board, Player) :-
    (
        row_win(Board, Player, _Row)
    ;
        col_win(Board, Player, _Col)
    ;
        diagonal_win(Board, Player)
    ),
    Player \= e.

cell(Board, Row, Col, Value) :-
    Index is ((Row - 1) * 3) + Col,
    nth1(Index, Board, Value).

row_win(Board, Player, Row) :-
    between(1, 3, Row),
    cell(Board, Row, 1, Player),
    cell(Board, Row, 2, Player),
    cell(Board, Row, 3, Player).

col_win(Board, Player, Col) :-
    between(1, 3, Col),
    cell(Board, 1, Col, Player),
    cell(Board, 2, Col, Player),
    cell(Board, 3, Col, Player).

diagonal_win(Board, Player) :-
    cell(Board, 1, 1, Player),
    cell(Board, 2, 2, Player),
    cell(Board, 3, 3, Player).

diagonal2_win(Board, Player) :-
    cell(Board, 1, 3, Player),
    cell(Board, 2, 2, Player),
    cell(Board, 3, 1, Player).


