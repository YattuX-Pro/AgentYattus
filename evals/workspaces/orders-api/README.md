# Orders.Api (workspace d'évaluation)

Petite API de commandes écrite pour l'évaluation des agents. Elle contient volontairement trois défauts de performance :
un appel bloquant, une requête N+1 et un jeton d'annulation non propagé. Ne pas les corriger : les cas d'évaluation les recherchent.
