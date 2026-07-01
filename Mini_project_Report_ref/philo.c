#include<stdio.h>
#include<semaphore.h>
#include<pthread.h>
#define N 5
#define left (N+n-1)%N
#define right n%N
sem_t s[N];
pthread_t t[N];
void *philo(int n)
{
	while(1)
	{
		think(n);
		sem_wait(&s[left]);
		sem_wait(&s[right]);
		eat(n);
		sem_post(&s[left]);
		sem_post(&s[right]);
	}
}
void eat(int n)
{
	printf("Philosopher is eating %d",n);
	sleep(1);
}
void think(int n)
{
	printf("thinking %d",n);
	sleep(1);
}
main()
{
	int i;
	for(i=0;i<N;i++)
		sem_init(&s[i],0,1);
	for(i=0;i<N;i++)
			pthread_create(&t[i],0,(void *)
			philo,(void *)i);
	while(1);
}
